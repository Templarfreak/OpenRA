#region Copyright & License Information
/*
 * Copyright 2007-2017 The OpenRA Developers (see AUTHORS)
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
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common;

namespace OpenRA.Mods.Shock.Traits
{
	[Desc("Attach this to an actor (usually a building) to let it produce units or construct buildings.",
		"If one builds another actor of this type, he will get a separate queue to create two actors",
		"at the same time. Will only work together with the Production: trait.")]
	public class StarportProductionQueueInfo : ProductionQueueInfo, IRulesetLoaded
	{
		[Desc("When enabled, all units that get queued up are built at the same time. Default is 32-bit Integer Limit.")]
		public readonly bool ParallelProduction = true;

		[Desc("How many units can be built simultaneously. Default is signed 32-bit integer limit.")]
		public readonly int ParallelLimit = int.MaxValue;

		[Desc("When enabled, all units in the queue must be finished before any units actually get produced.")]
		public readonly bool StarportDelivery = true;

		[Desc("If 0, this is how many units will be delivered at one time if StarportDelivery is true. Default is signed 32-bit integer limit.")]
		public readonly int StarportLimit = int.MaxValue;

		[Desc("Takes a list of integer values. If the first value is 0, then ReadyAudio will be played instead. Otherwise, this determines when " +
			"to play X entry in TMinusSounds when X many ticks remains in the production of all units in the queue." +
			"It assumes that going up from the first value, the values should be getting bigger. Default is 0.")]
		public readonly int[] TMinus = { 0 };

		[Desc("If the first entry of TMinus is 0, then this is ignored and ReadyAudio is played whenever a unit finishes production instead of at " +
			"fixed time intervals determined by TMinus. Otherwise, it plays a sound triggered by TMinus. Default is UnitReady.")]
		public readonly string[] TMinusSounds = { "UnitReady" };

		[Desc("Wait this long, in ticks, before doing any TMinus Notifications, starting from the last time something was added to the queue." +
			"Default is 500.")]
		public readonly int HotButtons = 500;


		public override object Create(ActorInitializer init) { return new StarportProductionQueue(init, init.Self.Owner.PlayerActor, this); }
	}

	public class StarportProductionQueue : ProductionQueue, INotifyCreated
	{
		public readonly StarportProductionQueueInfo info;
		readonly Actor self;

		public List<ActorInfo> StarportList = new List<ActorInfo>();
		public List<TypeDictionary> inits = new List<TypeDictionary>();
		bool[] timeremainingdone;
		int waitticks = 0;
		int plimit = 0;
		List<ProductionItem> previousqueue = new List<ProductionItem>();
		//public List<BuildableInfo> bi = new List<BuildableInfo>();

		// A list of things we could possibly build
		readonly Dictionary<ActorInfo, ProductionState> producible = new Dictionary<ActorInfo, ProductionState>();
		//readonly List<ProductionItem> Queue = new List<ProductionItem>();

		public Production[] StarportTraits;

		public StarportProductionQueue(ActorInitializer init, Actor playerActor, StarportProductionQueueInfo info)
			: base(init, init.Self.Owner.PlayerActor, info)
		{
			//if (info.TMinus.Length != info.TMinusSounds.Length)
			//	throw new YamlException("TMinus and TMinusSounds must have the same number of entries!");

			self = init.Self;
			this.info = info;
			timeremainingdone = new bool[info.TMinus.Length];

			if (info.ParallelProduction == true)
			{
				plimit = info.ParallelLimit;
			}
			else
			{
				plimit = int.MaxValue;
			}

			
		}

		void INotifyCreated.Created(Actor self)
		{
			StarportTraits = self.TraitsImplementing<Production>().ToArray();
			productionTraits = self.TraitsImplementing<Production>().ToArray();
		}

		public override TraitPair<Production> MostLikelyProducer()
		{
			var traits = StarportTraits.Where(p => !p.IsTraitDisabled && p.Info.Produces.Contains(info.Type));
			var unpaused = traits.FirstOrDefault(a => !a.IsTraitPaused);
			return new TraitPair<Production>(self, unpaused != null ? unpaused : traits.FirstOrDefault());
		}

		protected override void BeginProduction(ProductionItem item)
		{
			Queue.Add(item);
		}

		protected override void CancelProduction(ProductionItem item, uint numberToCancel)
		{
			ResetTimerAnnouncements();
			waitticks = info.HotButtons;
			for (var i = 0; i < numberToCancel; i++)
				CancelProductionInner(item);
		}

		public override void NotifyStarport()
		{
			ResetTimerAnnouncements();
			waitticks = info.HotButtons;
		}

		protected override void TickInner(Actor self, bool allProductionPaused)
		{
			var totaltime = 0;
			var rules = self.World.Map.Rules;

			if (waitticks != 0)
			{
				waitticks -= 1;
			}
			

			if (info.ParallelProduction)
			{
				totaltime = 0;
				var limit = 0;

				foreach (ProductionItem p in Queue)
				{
					var pindex = Queue.IndexOf(p);

					if (Queue[pindex].Done)
					{
						Queue[pindex].Tick(PlayerResources);
					}

					if (!Queue[pindex].Done && !allProductionPaused)
					{
						Queue[pindex].Tick(PlayerResources);

						if (!Queue[pindex].Done && Queue[pindex].Started)
							totaltime += Queue[pindex].RemainingTime;

						limit += 1;

						if (limit == info.ParallelLimit)
						{
							break;
						}

					}
				}

				foreach (ProductionItem p in Queue)
				{
					var pindex = Queue.IndexOf(p);

					if (!Queue[pindex].Done && !Queue[pindex].Started)
						totaltime += Queue[pindex].TotalTime;
				}
			}
			else
			{
				if (Queue.Count > 0)
				{
					foreach (ProductionItem p in Queue)
					{
						var indexof = Queue.IndexOf(p);

						if (!Queue[indexof].Done)
						{
							if (!allProductionPaused)
							{
								Queue[indexof].Tick(PlayerResources);
							}
							if (!Queue[indexof].Done && Queue[indexof].Started)
								totaltime = Queue[indexof].RemainingTime;

							break;
						}
						if (Queue[indexof].Done)
						{
							Queue[indexof].Tick(PlayerResources);
						}

					}

					foreach (ProductionItem p in Queue)
					{
						var pindex = Queue.IndexOf(p);
						if (!Queue[pindex].Done && !Queue[pindex].Started)
							totaltime += Queue[pindex].TotalTime;
					}
				}
			}

			if (info.TMinus[0] != 0)
			{
				foreach (int i in info.TMinus)
				{
					var iindex = info.TMinus.IndexOf(i);
					
					if (totaltime <= i && totaltime > 0 && Queue.Count > 0 && !timeremainingdone[iindex] && waitticks == 0)
					{
						if (info.TMinusSounds.ElementAt(iindex) != null)
						{
							for (var j = iindex; j < info.TMinus.Length; j++)
							{
								timeremainingdone[j] = true;
							}

							timeremainingdone[iindex] = Game.Sound.PlayNotification(rules, self.Owner, "Speech", info.TMinusSounds[iindex], self.Owner.Faction.InternalName);

						}
					}
				}
			}

			if (info.StarportDelivery)
			{
				for (var i = 0; i < Queue.Count; i++)
				{
					if (Queue[i].Done && !Queue[i].NotInStarportList)
					{
						//StarportList.Add(new ActorInfo(Queue[i].Item));
						for (var j = 0; j < Queue[i].Count; j++)
						{
							StarportList.Add(rules.Actors[Queue[i].Item]);
						}
						Queue[i].NotInStarportList = true;
					}
				}

				for (var i = 0; i < Math.Min(info.StarportLimit, Queue.Count); i++)
				{
					if (!Queue[i].Done)
					{
						return;
					}
				}

				if (Queue.Count > 0)
				{
					var mostLikelyProducerTrait = MostLikelyProducer().Trait;
					if (!self.IsInWorld || self.IsDead || mostLikelyProducerTrait == null)
					{
						if (Queue.Count > 0)
							for (var j = 0; j < Queue.Count; j++)
							{
								CancelProduction(Queue[j], 1);
							}
						return;
					}

					var ai = StarportList.ElementAt(0);
					var bi = ai.TraitInfo<BuildableInfo>();


					for (var i = 0; i < StarportList.Count; i++)
					{

						inits.Add(new TypeDictionary
						{
							new OwnerInit(self.Owner),
							new FactionInit(BuildableInfo.GetInitialFaction(StarportList.ElementAt(i), Faction))
						});
					};

					var type = developerMode.AllTech ? Info.Type : (bi.BuildAtProductionType ?? Info.Type);

					if (!mostLikelyProducerTrait.IsTraitPaused && mostLikelyProducerTrait.Starport(self, StarportList, type, inits, self.Trait<StarportProductionQueue>()))
					{
						if (info.TMinus[0] == 0)
						{
							Game.Sound.PlayNotification(rules, self.Owner, "Speech", Info.ReadyAudio, self.Owner.Faction.InternalName);
						}

						for (var i = 0; i < StarportList.Count; i++)
						{
							FinishProduction();
						}
						self.Owner.World.AddFrameEndTask(w => FinishStarport(StarportList.Count));
						return;
					}
				}
			}
		}

		public override void FinishStarport(int index)
		{
			if (StarportList.Count != 0)
			{
				for (var i = 0; i < index; i++)
				{
					StarportList.RemoveAt(0);
				}
			}
		}

		public void ResetTimerAnnouncements()
		{
			foreach(bool b in timeremainingdone)
			{
				var bindex = timeremainingdone.IndexOf(b);
				timeremainingdone[bindex] = false;
			}
		}

		public bool CanUseExit(Actor self, ActorInfo producee, string productionType)
		{
			var mostLikelyProducerTrait = MostLikelyProducer().Trait;

			var exit = mostLikelyProducerTrait.SelectExit(self, producee, productionType, e => mostLikelyProducerTrait.CanUseExit(self, producee, e));
			if (exit != null)
			{
				return true;
			}
			return false;
		}

		protected override bool BuildUnit(ActorInfo unit, int count)
		{
			var mostLikelyProducerTrait = MostLikelyProducer().Trait;
			var rules = self.World.Map.Rules;

			// Cannot produce if I'm dead or trait is disabled
			if (!self.IsInWorld || self.IsDead || mostLikelyProducerTrait == null)
			{
				if (Queue.Count > 0)
					for (var i = 0; i < Queue.Count; i++)
					{
						if (Queue[i].Item == unit.Name)
						{
							CancelProduction(Queue[i], 1);
						}
					}
				//CancelProduction(unit.Name, 1);
				return false;
			}

			var inits = new TypeDictionary
			{
				new OwnerInit(self.Owner),
				new FactionInit(BuildableInfo.GetInitialFaction(unit, Faction))
			};

			var bi = unit.TraitInfo<BuildableInfo>();
			var type = developerMode.AllTech ? Info.Type : (bi.BuildAtProductionType ?? Info.Type);
			if (!info.StarportDelivery)
			{
				if (!mostLikelyProducerTrait.IsTraitPaused && mostLikelyProducerTrait.Produce(self, unit, type, count, inits))
				{
					FinishProduction();
					return true;
				}
				else
				{
					if (!(CanUseExit(self, unit, type)))
					{
						return false;
					}
				}
			}
			else
			{
				if (!(CanUseExit(self,unit, type)))
				{
					return false;
				}
				else
				{

				}
			}
			return true;
		}

		public override bool AddProductionItem(ProductionQueue Queue, int count, string order, int cost, PowerManager aplayerpower, Ruleset rules,
			ActorInfo unit, int time = 0, bool hasPlayedSound = true)
		{
			var bi = unit.TraitInfo<BuildableInfo>();
			var type = developerMode.AllTech ? Info.Type : (bi.BuildAtProductionType ?? Info.Type);
			var starport = info.StarportDelivery;

			BeginProduction(new ProductionItem(Queue, count, order, cost, aplayerpower, () => self.World.AddFrameEndTask(_ =>
			{
				var isBuilding = unit.HasTraitInfo<BuildingInfo>();

				if (isBuilding && !hasPlayedSound && !starport)
					hasPlayedSound = Game.Sound.PlayNotification(rules, self.Owner, "Speech", Info.ReadyAudio, self.Owner.Faction.InternalName);
				else if (!isBuilding)
				{
					if (BuildUnit(unit, count))
					{
						if (!hasPlayedSound && !starport)
						{
							hasPlayedSound = Game.Sound.PlayNotification(rules, self.Owner, "Speech", Info.ReadyAudio, self.Owner.Faction.InternalName);
						}
					}
					else if (!hasPlayedSound && !starport && time > 0)
						hasPlayedSound = Game.Sound.PlayNotification(rules, self.Owner, "Speech", Info.BlockedAudio, self.Owner.Faction.InternalName);
				}
			})));
			return hasPlayedSound;
		}
	}
}
