#region Copyright & License Information
/*
 * Copyright 2007-2018 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Attach this to an actor (usually a building) to let it produce units or construct buildings.",
		"If one builds another actor of this type, he will get a separate queue to create two actors",
		"at the same time. Will only work together with the Production: trait.")]
	public class ProductionQueueInfo : ITraitInfo
	{
		[FieldLoader.Require]
		[Desc("What kind of production will be added (e.g. Building, Infantry, Vehicle, ...)")]
		public readonly string Type = null;

		[Desc("Group queues from separate buildings together into the same tab.")]
		public readonly string Group = null;

		[Desc("Only enable this queue for certain factions.")]
		public readonly HashSet<string> Factions = new HashSet<string>();

		[Desc("Should the prerequisite remain enabled if the owner changes?")]
		public readonly bool Sticky = true;

		[Desc("Should right clicking on the icon instantly cancel the production instead of putting it on hold?")]
		public readonly bool DisallowPaused = false;

		[Desc("This percentage value is multiplied with actor cost to translate into build time (lower means faster).")]
		public readonly int BuildDurationModifier = 100;

		[Desc("Maximum number of a single actor type that can be queued (0 = infinite).")]
		public readonly int ItemLimit = 999;

		[Desc("Maximum number of items that can be queued across all actor types (0 = infinite).")]
		public readonly int QueueLimit = 0;

		[Desc("The build time is multiplied with this value on low power.")]
		public readonly int LowPowerSlowdown = 3;

		[Desc("Notification played when production is complete.",
			"The filename of the audio is defined per faction in notifications.yaml.")]
		public readonly string ReadyAudio = "UnitReady";

		[Desc("Notification played when you can't train another actor",
			"when the build limit exceeded or the exit is jammed.",
			"The filename of the audio is defined per faction in notifications.yaml.")]
		public readonly string BlockedAudio = "NoBuild";

		[Desc("Notification played when you can't queue another actor",
			"when the queue length limit is exceeded.",
			"The filename of the audio is defined per faction in notifications.yaml.")]
		public readonly string LimitedAudio = null;

		[Desc("Notification played when user clicks on the build palette icon.",
			"The filename of the audio is defined per faction in notifications.yaml.")]
		public readonly string QueuedAudio = "Training";

		[Desc("Notification played when player right-clicks on the build palette icon.",
			"The filename of the audio is defined per faction in notifications.yaml.")]
		public readonly string OnHoldAudio = "OnHold";

		[Desc("Notification played when player right-clicks on a build palette icon that is already on hold.",
			"The filename of the audio is defined per faction in notifications.yaml.")]
		public readonly string CancelledAudio = "Cancelled";

		public virtual object Create(ActorInitializer init) { return new ProductionQueue(init, init.Self.Owner.PlayerActor, this); }

		public void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (LowPowerSlowdown <= 0)
				throw new YamlException("Production queue must have LowPowerSlowdown of at least 1.");
		}
	}

	public class ProductionQueue : IResolveOrder, ITick, ITechTreeElement, INotifyOwnerChanged, INotifyKilled, INotifySold, ISync, INotifyTransform, INotifyCreated
	{
		public readonly ProductionQueueInfo Info;
		readonly Actor self;

		// A list of things we could possibly build
		readonly Dictionary<ActorInfo, ProductionState> producible = new Dictionary<ActorInfo, ProductionState>();
		public readonly List<ProductionItem> queue = new List<ProductionItem>();
		readonly IEnumerable<ActorInfo> allProducibles;
		readonly IEnumerable<ActorInfo> buildableProducibles;

		public Production[] productionTraits;

		// Will change if the owner changes
		PowerManager playerPower;
		public PlayerResources playerResources;
		protected DeveloperMode developerMode;

		public Actor Actor { get { return self; } }

		[Sync] public int QueueLength { get { return queue.Count; } }
		[Sync] public int CurrentRemainingCost { get { return QueueLength == 0 ? 0 : queue[0].RemainingCost; } }
		[Sync] public int CurrentRemainingTime { get { return QueueLength == 0 ? 0 : queue[0].RemainingTime; } }
		[Sync] public int CurrentSlowdown { get { return QueueLength == 0 ? 0 : queue[0].Slowdown; } }
		[Sync] public bool CurrentPaused { get { return QueueLength != 0 && queue[0].Paused; } }
		[Sync] public bool CurrentDone { get { return QueueLength != 0 && queue[0].Done; } }
		[Sync] public bool Enabled { get; protected set; }

		public string Faction { get; private set; }
		[Sync] public bool IsValidFaction { get; private set; }

		public ProductionQueue(ActorInitializer init, Actor playerActor, ProductionQueueInfo info)
		{
			self = init.Self;
			Info = info;
			playerResources = playerActor.Trait<PlayerResources>();
			playerPower = playerActor.Trait<PowerManager>();
			developerMode = playerActor.Trait<DeveloperMode>();

			Faction = init.Contains<FactionInit>() ? init.Get<FactionInit, string>() : self.Owner.Faction.InternalName;
			IsValidFaction = !info.Factions.Any() || info.Factions.Contains(Faction);
			Enabled = IsValidFaction;

			CacheProducibles(playerActor);
			allProducibles = producible.Where(a => a.Value.Buildable || a.Value.Visible).Select(a => a.Key);
			buildableProducibles = producible.Where(a => a.Value.Buildable).Select(a => a.Key);
		}

		void INotifyCreated.Created(Actor self)
		{
			productionTraits = self.TraitsImplementing<Production>().ToArray();
		}

		protected void ClearQueue()
		{
			if (queue.Count == 0)
				return;

			// Refund the current item
			playerResources.GiveCash(queue[0].TotalCost - queue[0].RemainingCost);
			queue.Clear();
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			ClearQueue();

			playerPower = newOwner.PlayerActor.Trait<PowerManager>();
			playerResources = newOwner.PlayerActor.Trait<PlayerResources>();
			developerMode = newOwner.PlayerActor.Trait<DeveloperMode>();

			if (!Info.Sticky)
			{
				Faction = self.Owner.Faction.InternalName;
				IsValidFaction = !Info.Factions.Any() || Info.Factions.Contains(Faction);
			}

			// Regenerate the producibles and tech tree state
			oldOwner.PlayerActor.Trait<TechTree>().Remove(this);
			CacheProducibles(newOwner.PlayerActor);
			newOwner.PlayerActor.Trait<TechTree>().Update();
		}

		void INotifyKilled.Killed(Actor killed, AttackInfo e) { if (killed == self) { ClearQueue(); Enabled = false; } }
		void INotifySold.Selling(Actor self) { ClearQueue(); Enabled = false; }
		void INotifySold.Sold(Actor self) { }

		void INotifyTransform.BeforeTransform(Actor self) { ClearQueue(); Enabled = false; }
		void INotifyTransform.OnTransform(Actor self) { }
		void INotifyTransform.AfterTransform(Actor self) { }

		void CacheProducibles(Actor playerActor)
		{
			producible.Clear();
			if (!Enabled)
				return;

			var ttc = playerActor.Trait<TechTree>();

			foreach (var a in AllBuildables(Info.Type))
			{
				var bi = a.TraitInfo<BuildableInfo>();

				producible.Add(a, new ProductionState());
				ttc.Add(a.Name, bi.Prerequisites, bi.BuildLimit, this);
			}
		}

		IEnumerable<ActorInfo> AllBuildables(string category)
		{
			return self.World.Map.Rules.Actors.Values
				.Where(x =>
					x.Name[0] != '^' &&
					x.HasTraitInfo<BuildableInfo>() &&
					x.TraitInfo<BuildableInfo>().Queue.Contains(category));
		}

		public void PrerequisitesAvailable(string key)
		{
			producible[self.World.Map.Rules.Actors[key]].Buildable = true;
		}

		public void PrerequisitesUnavailable(string key)
		{
			producible[self.World.Map.Rules.Actors[key]].Buildable = false;
		}

		public void PrerequisitesItemHidden(string key)
		{
			producible[self.World.Map.Rules.Actors[key]].Visible = false;
		}

		public void PrerequisitesItemVisible(string key)
		{
			producible[self.World.Map.Rules.Actors[key]].Visible = true;
		}

		public ProductionItem CurrentItem()
		{
			return queue.ElementAtOrDefault(0);
		}

		//Some new search functions. This only returns non-paused units in the queue.
		public ProductionItem MostRecentNotPausedItem()
		{
			//return queue.ElementAtOrDefault(0);
			for (var i = 0; i < queue.Count; i++)
			{
				if (!queue[i].Paused)
				{
					return queue[i];
				}
			}
			return queue.ElementAtOrDefault(0);
		}

		//Not paused, done, has started. If anything is found to be a building, we just always return said building otherwise
		//Otherwise it could give us trouble if it is used in something like ProductionPaletteWidget.
		public ProductionItem MostRecentStandard()
		{
			for (var i = 0; i < queue.Count; i++)
			{
				var rules = self.World.Map.Rules;
				var unit = rules.Actors[queue[i].Item];
				var isbuilding = unit.HasTraitInfo<BuildingInfo>();

				if (!queue[i].Paused && !queue[i].Done && queue[i].Started && !isbuilding)
				{
					return queue[i];
				}
				else if (isbuilding)
				{
					return queue[i];
				}
			}
			return queue.ElementAtOrDefault(0);
		}

		//Same as before but here we also check if the queued item's name is equal to whatever item name we're given.
		//I was using this in ProductionPaletteWidget but now there is new logic there. This may still be useful later though.
		public ProductionItem MostRecentStandard(string item)
		{
			for (var i = 0; i < queue.Count; i++)
			{
				var rules = self.World.Map.Rules;
				var unit = rules.Actors[queue[i].Item];
				var isbuilding = unit.HasTraitInfo<BuildingInfo>();

				if (!queue[i].Paused && !queue[i].Done && queue[i].Started && !isbuilding && queue[i].Item == item)
				{
					return queue[i];
				}
				else if (isbuilding && queue[i].Item == item)
				{
					return queue[i];
				}
			}
			return queue.ElementAtOrDefault(0);
		}

		public virtual IEnumerable<ProductionItem> AllQueued()
		{
			return queue;
		}

		public virtual IEnumerable<ActorInfo> AllItems()
		{
			if (developerMode.AllTech)
				return producible.Keys;

			return allProducibles;
		}

		public virtual IEnumerable<ActorInfo> BuildableItems()
		{
			if (!Enabled)
				return Enumerable.Empty<ActorInfo>();
			if (developerMode.AllTech)
				return producible.Keys;

			return buildableProducibles;
		}

		public bool CanBuild(ActorInfo actor)
		{
			ProductionState ps;
			if (!producible.TryGetValue(actor, out ps))
				return false;

			return ps.Buildable || developerMode.AllTech;
		}

		void ITick.Tick(Actor self)
		{
			Tick(self);
		}

		protected virtual void Tick(Actor self)
		{
			// PERF: Avoid LINQ when checking whether all production traits are disabled/paused
			var anyEnabledProduction = false;
			var anyUnpausedProduction = false;
			foreach (var p in productionTraits)
			{
				anyEnabledProduction |= !p.IsTraitDisabled;
				anyUnpausedProduction |= !p.IsTraitPaused;
			}

			if (!anyEnabledProduction)
				ClearQueue();

			Enabled = IsValidFaction && anyEnabledProduction;

			TickInner(self, !anyUnpausedProduction);
		}

		protected virtual void TickInner(Actor self, bool allProductionPaused)
		{
			while (queue.Count > 0 && BuildableItems().All(b => b.Name != queue[0].Item))
			{
				// Refund what's been paid so far
				playerResources.GiveCash(queue[0].TotalCost - queue[0].RemainingCost);
				FinishProduction();
			}

			if (queue.Count > 0 && !allProductionPaused)
				queue[0].Tick(playerResources);
		}

		public bool CanQueue(ActorInfo actor, out string notificationAudio)
		{
			notificationAudio = Info.BlockedAudio;

			var bi = actor.TraitInfoOrDefault<BuildableInfo>();
			if (bi == null)
				return false;

			if (!developerMode.AllTech)
			{
				if (Info.QueueLimit > 0 && queue.Count >= Info.QueueLimit)
				{
					notificationAudio = Info.LimitedAudio;
					return false;
				}

				var queueCount = queue.Count(i => i.Item == actor.Name);
				if (Info.ItemLimit > 0 && queueCount >= Info.ItemLimit)
				{
					notificationAudio = Info.LimitedAudio;
					return false;
				}

				if (bi.BuildLimit > 0)
				{
					var owned = self.Owner.World.ActorsHavingTrait<Buildable>()
						.Count(a => a.Info.Name == actor.Name && a.Owner == self.Owner);
					if (queueCount + owned >= bi.BuildLimit)
						return false;
				}
			}

			notificationAudio = Info.QueuedAudio;
			return true;
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (!Enabled)
				return;

			var rules = self.World.Map.Rules;
			switch (order.OrderString)
			{
				case "StartProduction":
					//We call this so StarportProductionQueue can know that we've added, or at least attempted to add,
					//Something to the queue so we can reset waitticks so the notification callouts don't overlap
					//With other sounds and so we can re-check if we need to say other sounds we originally thought
					//we didn't need to (because the queue updated).
					NotifyStarport();

					var unit = rules.Actors[order.TargetString];
					var bi = unit.TraitInfo<BuildableInfo>();
					int owned = 0;
					int inQueue = 0;
					var counttobuild = bi.Count;
					var fromLimit = int.MaxValue;
					var hasPlayedSound = false;

					// Not built by this queue
					if (!bi.Queue.Contains(Info.Type))
						return;

					// You can't build that
					if (BuildableItems().All(b => b.Name != order.TargetString))
						return;

					// Check if the player is trying to build more units that they are allowed
					if (!developerMode.AllTech)
					{
						if (Info.QueueLimit > 0)
							fromLimit = Info.QueueLimit - queue.Count;

						if (Info.ItemLimit > 0)
							fromLimit = Math.Min(fromLimit, Info.ItemLimit - queue.Count(i => i.Item == order.TargetString));

						if (bi.BuildLimit > 0)
						{
							inQueue = queue.Count(pi => pi.Item == order.TargetString);
							owned = self.Owner.World.ActorsHavingTrait<Buildable>().Count(a => a.Info.Name == order.TargetString && a.Owner == self.Owner);
							fromLimit = bi.BuildLimit - ((inQueue * bi.Count) + owned);
							//We feed this into Produce and Produce loops DoProduction counttobuild many times.
							//Unless we're a Starport, in which case this many units gets added to the list instead of only 1.
							counttobuild = Math.Min(bi.BuildLimit - (owned + inQueue * bi.Count), bi.Count);
						}

						if (fromLimit <= 0)
							return;
					}

					var valued = unit.TraitInfoOrDefault<ValuedInfo>();
					float div = (float)((float)counttobuild / (float)bi.Count);
					int result = (int)(valued.Cost * div);
					var cost = valued != null ? result : 0;
					var time = GetBuildTime(unit, bi);
					var amountToBuild = Math.Min(fromLimit, order.ExtraData);

					for (var n = 0; n < amountToBuild; n++)
					{
						//This allows us to override AddProductionItem so we can change this through another ProductionQueue.
						//For instance we make some minor changes to this for the StarportQueue.
						hasPlayedSound = AddProductionItem(this, counttobuild, order.TargetString, cost, playerPower, rules, unit, time, hasPlayedSound);
					}

					break;
				case "PauseProduction":
					//We pause *all* units in production on the icon.
					if (queue.Count > 0)
						for (var i = 0; i < queue.Count; i++)
						{
							if (queue[i].Item == order.TargetString)
							{
								queue[i].Pause(order.ExtraData != 0);
							}
						}
					break;
				case "CancelProduction":
					//But we only cancel ExtraData many units of the queue, then break.
					//The setup for this (and PauseProduction) has the added benefit of always pausing the ones
					//that are the oldest in the queue (so they appear closer to or are 0 index).
					//This means that the ones that are the most complete gets paused.
					if (order.ExtraData > 1)
					{
						if (queue.Count > 0)
							for (var i = 0; i < queue.Count; i++)
							{
								if (queue[i].Item == order.TargetString)
								{
									CancelProduction(queue[i], order.ExtraData);
									return;
								}
							}
					}
					else
					{
						//I think this is useless now, but I don't want to touch it.
						//All of these For loops should probably also be Foreach's. That's just me not knowing what I'm doing.
						if (queue.Count > 0)
							for (var i = 0; i < queue.Count; i++)
							{
								if (queue[i].Item == order.TargetString)
								{
									CancelProduction(queue[i], order.ExtraData);
									return;
								}
							}
					}
					break;
			}
		}

		//Any new ProductionQueue trait can override this to change what Action is given to the ProductionItem (as well as lots of other behavior
		// here)
		public virtual bool AddProductionItem(ProductionQueue queue, int count, string order, int cost, PowerManager playerpower, Ruleset rules,
			ActorInfo unit, int time = 0, bool hasPlayedSound = false)
		{
			BeginProduction(new ProductionItem(queue, count, order, cost, playerPower, () => self.World.AddFrameEndTask(_ =>
			{
				var isBuilding = unit.HasTraitInfo<BuildingInfo>();

				if (isBuilding && !hasPlayedSound)
					hasPlayedSound = Game.Sound.PlayNotification(rules, self.Owner, "Speech", Info.ReadyAudio, self.Owner.Faction.InternalName);
				else if (!isBuilding)
				{
					if (BuildUnit(unit, count))
					{
						if (!hasPlayedSound)
						{
							hasPlayedSound = Game.Sound.PlayNotification(rules, self.Owner, "Speech", Info.ReadyAudio, self.Owner.Faction.InternalName);
						}
					}
					else if (!hasPlayedSound && time > 0)
						hasPlayedSound = Game.Sound.PlayNotification(rules, self.Owner, "Speech", Info.BlockedAudio, self.Owner.Faction.InternalName);
				}
			})));
			return hasPlayedSound;
		}

		public virtual int GetBuildTime(ActorInfo unit, BuildableInfo bi)
		{
			if (developerMode.FastBuild)
				return 0;

			var time = bi.BuildDuration;
			if (time == -1)
			{
				var valued = unit.TraitInfoOrDefault<ValuedInfo>();
				time = valued != null ? valued.Cost : 0;
			}

			time = time * bi.BuildDurationModifier * Info.BuildDurationModifier / 10000;
			return time;
		}

		protected virtual void CancelProduction(ProductionItem itemName, uint numberToCancel)
		{
			for (var i = 0; i < numberToCancel; i++)
				if (!CancelProductionInner(itemName))
					break;
		}

		public bool CancelProductionInner(ProductionItem itemName)
		{
			// Refund what has been paid
			//We only cancel the top-most units that fit our conditions.
			if (queue.Count != 0)
			{
				for (var i = 0; i < queue.Count; i++)
				{
					var itemstring = queue[i];
					if (queue.ElementAt(i) == itemName)
					{
						playerResources.GiveCash(itemstring.TotalCost - itemstring.RemainingCost);
						queue.RemoveAt(i);
						return true;
					}
				}
			}
			return false;
		}

		public void FinishProduction()
		{
			//Or finish them.
			if (queue.Count != 0)
			{
				for (var i = 0; i < queue.Count; i++)
				{
					if (queue.ElementAt(i).Done == true)
					{
						queue.RemoveAt(i);
					}
				}
			}
		}

		//Stuff to help update and refresh things on Starports.
		public virtual void FinishStarport(int index) { }
		public virtual void NotifyStarport() { }

		protected virtual void BeginProduction(ProductionItem item)
		{
			queue.Add(item);
		}

		// Returns the actor/trait that is most likely (but not necessarily guaranteed) to produce something in this queue
		public virtual TraitPair<Production> MostLikelyProducer()
		{
			var traits = productionTraits.Where(p => !p.IsTraitDisabled && p.Info.Produces.Contains(Info.Type));
			var unpaused = traits.FirstOrDefault(a => !a.IsTraitPaused);
			return new TraitPair<Production>(self, unpaused != null ? unpaused : traits.FirstOrDefault());
		}

		// Builds a unit from the actor that holds this queue (1 queue per building)
		// Returns false if the unit can't be built
		protected virtual bool BuildUnit(ActorInfo unit, int count)
		{
			var mostLikelyProducerTrait = MostLikelyProducer().Trait;

			// Cannot produce if I'm dead or trait is disabled
			if (!self.IsInWorld || self.IsDead || mostLikelyProducerTrait == null)
			{
				if (queue.Count > 0)
					for (var i = 0; i < queue.Count; i++)
					{
						if (queue[i].Item == unit.Name)
						{
							CancelProduction(queue[i], 1);
						}
					}
				return false;
			}

			var inits = new TypeDictionary
			{
				new OwnerInit(self.Owner),
				new FactionInit(BuildableInfo.GetInitialFaction(unit, Faction))
			};

			var bi = unit.TraitInfo<BuildableInfo>();
			var type = developerMode.AllTech ? Info.Type : (bi.BuildAtProductionType ?? Info.Type);

			if (!mostLikelyProducerTrait.IsTraitPaused && mostLikelyProducerTrait.Produce(self, unit, type, count, inits))
			{
				FinishProduction();
				return true;
			}

			return false;
		}
	}

	public class ProductionState
	{
		public bool Visible = true;
		public bool Buildable = false;
	}

	public class ProductionItem
	{
		//made some changes here too. It keeps track of the count of the item and always knows its total build time now.
		public readonly string Item;
		public readonly ProductionQueue Queue;
		public readonly int TotalCost;	
		public readonly Action OnComplete;
		public int count;
		public bool NotInStarportList = false;

		public int TotalTime { get; private set; }
		public int RemainingTime { get; private set; }
		public int RemainingCost { get; private set; }
		public int RemainingTimeActual
		{
			get
			{
				return (pm.PowerState == PowerState.Normal) ? RemainingTime :
					RemainingTime * Queue.Info.LowPowerSlowdown;
			}
		}

		public bool Paused { get; private set; }
		public bool Done { get; private set; }
		public bool Started { get; private set; }
		public int Slowdown { get; private set; }

		readonly ActorInfo ai;
		readonly BuildableInfo bi;
		readonly PowerManager pm;

		public ProductionItem(ProductionQueue queue, int count, string item, int cost, PowerManager pm, Action onComplete)
		{
			Item = item;
			RemainingTime = TotalTime = 1;
			RemainingCost = TotalCost = cost;
			OnComplete = onComplete;
			this.count = count;
			Queue = queue;
			this.pm = pm;
			ai = Queue.Actor.World.Map.Rules.Actors[Item];
			bi = ai.TraitInfo<BuildableInfo>();
			TotalTime = Math.Max(1, Queue.GetBuildTime(ai, bi));
			RemainingTime = TotalTime;
		}

		public void Tick(PlayerResources pr)
		{
			if (!Started)
			{
				var time = Queue.GetBuildTime(ai, bi);
				if (time > 0)
					RemainingTime = TotalTime = time;

				Started = true;
			}

			if (Done)
			{
				if (OnComplete != null)
					OnComplete();

				return;
			}

			if (Paused)
				return;

			if (pm.PowerState != PowerState.Normal)
			{
				if (--Slowdown <= 0)
					Slowdown = Queue.Info.LowPowerSlowdown;
				else
					return;
			}

			var costThisFrame = RemainingCost / RemainingTime;
			if (costThisFrame != 0 && !pr.TakeCash(costThisFrame, true))
				return;

			RemainingCost -= costThisFrame;
			RemainingTime -= 1;
			if (RemainingTime > 0)
				return;

			Done = true;
		}

		public void Pause(bool paused) { Paused = paused; }
	}
}
