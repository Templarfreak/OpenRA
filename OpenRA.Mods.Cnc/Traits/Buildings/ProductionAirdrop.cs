#region Copyright & License Information
/*
 * Copyright 2007-2019 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Linq;
using System.Collections.Generic;
using OpenRA.Activities;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	[Desc("Deliver the unit in production via skylift.")]
	public class ProductionAirdropInfo : ProductionInfo
	{
		[NotificationReference("Speech")]
		public readonly string ReadyAudio = "Reinforce";

		[Desc("Cargo aircraft used for delivery. Must have the `Aircraft` trait.")]
		[ActorReference(typeof(AircraftInfo))] public readonly string ActorType = "c17";

		[Desc("Takes up, down, left, right, or space. Customizes which direction the actor approaches from. \"space\" has special functionality." +
			"Adjust the CruiseAltitude of your Actor to control how far away in space it comes from." +
			"Default is right.")]
		public readonly string StartingPosition = "right";

		[Desc("Takes up, down, left, right, or space. Customizes which direction the actor leaves towards. Default is left.")]
		public readonly string EndingPosition = "left";

		[Desc("Scales how far away the actor spawns. 2.5 = map size / 2.5, so they spawn 2 5ths as far away. Default is 1.")]
		public readonly float DistanceScale = 1;

		[Desc("Controls how long the actor waits before creating the units. Default is 0.")]
		public readonly int WaitStart = 0;

		[Desc("Controls how long the actor waits before leaving after creating the units. Default is 0.")]
		public readonly int WaitEnd = 0;

		[Desc("If greater than 0, units that build more than one at a time can have a wait put between each one.")]
		public readonly int WaitBetweenUnits = 0;

		[Desc("Makes the ActorType ignore its facing, so it immediately moves towards directions. False is normal flying behavior. " +
			"Default is false. If your Aircraft has VTL = true, that takes priority over this.")]
		public readonly bool SpecialFly = false;

		public override object Create(ActorInitializer init) { return new ProductionAirdrop(init, this); }
	}

	class ProductionAirdrop : Production
	{
		readonly ProductionAirdropInfo info;
		public ProductionAirdrop(ActorInitializer init, ProductionAirdropInfo info)
			: base(init, info)
		{
			this.info = info;
		}

		public override bool Produce(Actor self, ActorInfo producee, string productionType, int count, TypeDictionary inits)
		{
			if (IsTraitDisabled || IsTraitPaused)
				return false;

			var info = (ProductionAirdropInfo)Info;
			var owner = self.Owner;
			var aircraftInfo = self.World.Map.Rules.Actors[info.ActorType].TraitInfo<AircraftInfo>();

			// WDist required to take off or land
			var landDistance = aircraftInfo.CruiseAltitude.Length * 1024 / aircraftInfo.MaximumPitch.Tan();

			var startPos = self.Location + new CVec(owner.World.Map.Bounds.Width, 0);

			if (info.StartingPosition == "right")
			{
				startPos = self.Location + new CVec((int)((float)(owner.World.Map.Bounds.Width) / (float)(info.DistanceScale)), 0);
			}
			else if (info.StartingPosition == "left")
			{
				startPos = self.Location - new CVec((int)((float)owner.World.Map.Bounds.Width / (float)(info.DistanceScale)), 0);
			}
			else if (info.StartingPosition == "up")
			{
				startPos = self.Location - new CVec((int)((float)(owner.World.Map.Bounds.Height) / (float)(info.DistanceScale)), 0);
			}
			else if (info.StartingPosition == "down")
			{
				startPos = self.Location + new CVec((int)((float)(owner.World.Map.Bounds.Height) / (float)(info.DistanceScale)), 0);
			}
			else if (info.StartingPosition == "space")
			{
				startPos = self.Location;
			}

			var endPos = new CPos(owner.World.Map.Bounds.Left - 2 * landDistance / 1024, self.Location.Y);

			if (info.EndingPosition == "right")
			{
				endPos = new CPos(owner.World.Map.Bounds.Right, self.Location.Y);
			}
			if (info.EndingPosition == "left")
			{
				endPos = new CPos(owner.World.Map.Bounds.Left, self.Location.Y);
			}
			if (info.EndingPosition == "up")
			{
				endPos = new CPos(self.Location.X, owner.World.Map.Bounds.Top);
			}
			if (info.EndingPosition == "down")
			{
				endPos = new CPos(self.Location.X, owner.World.Map.Bounds.Bottom);
			}
			if (info.EndingPosition == "space")
			{
				endPos = self.Location;
			}

			// Assume a single exit point for simplicity
			var exit = self.Info.TraitInfos<ExitInfo>().First();

			foreach (var tower in self.TraitsImplementing<INotifyDelivery>())
				tower.IncomingDelivery(self);

			owner.World.AddFrameEndTask(w =>
			{
				if (!self.IsInWorld || self.IsDead)
					return;

				var exittrait = self.Info.TraitInfos<ExitInfo>().First().SpawnOffset;

				Actor actor;

				if (info.StartingPosition == "space")
				{
					actor = w.CreateActor(info.ActorType, new TypeDictionary
					{
						new CenterPositionInit(new WPos(self.CenterPosition.X + exittrait.X,self.CenterPosition.Y + exittrait.Y,aircraftInfo.CruiseAltitude.Length)),
						new OwnerInit(owner),
						new FacingInit(64)
					});
				}
				else
				{
					actor = w.CreateActor(info.ActorType, new TypeDictionary
					{
						new CenterPositionInit(w.Map.CenterOfCell(startPos) + new WVec(WDist.Zero, WDist.Zero, aircraftInfo.CruiseAltitude)),
						new OwnerInit(owner),
						new FacingInit(64)
					});
				}
				ActorInfo infotype = actor.Info;
				var aircraftinfo = infotype.TraitInfo<AircraftInfo>();

				if (info.StartingPosition == "space")
				{
					actor.QueueActivity(new HeliLand(actor, false));
				}
				else
				{
					if (aircraftinfo.VTOL)
					{
						actor.QueueActivity(new HeliFly(actor, Target.FromPos(self.CenterPosition + new WVec(landDistance, 0, 0))));
						actor.QueueActivity(new Land(actor, Target.FromActor(self), false));
					}
					else
					{
						actor.QueueActivity(new Fly(actor, Target.FromPos(self.CenterPosition + new WVec(landDistance, 0, 0))));
						actor.QueueActivity(new Land(actor, Target.FromActor(self), false));
					}
				}

				bool sound = false;
				actor.QueueActivity(new Wait(info.WaitStart + 1));
				for (var i = 0; i < count; i++)
				{
					actor.QueueActivity(new Wait(info.WaitBetweenUnits + 1));
					actor.QueueActivity(new CallFunc(() =>
					{
						if (!self.IsInWorld || self.IsDead)
							return;

						foreach (var cargo in self.TraitsImplementing<INotifyDelivery>())
							cargo.Delivered(self);

						self.World.AddFrameEndTask(ww => DoProduction(self, producee, exit, productionType, inits));
						if (sound == false)
						{
							sound = Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", info.ReadyAudio, self.Owner.Faction.InternalName);
						}

					}));
				}

				actor.QueueActivity(new Wait(info.WaitEnd + 1));

				if (info.EndingPosition == "space")
				{
					var move = actor.Trait<IMove>();
					actor.QueueActivity(new ScriptedTakeOff(actor, new AttackMoveActivity(actor, () => move.MoveTo(new CPos(self.CenterPosition.X + exittrait.X, self.CenterPosition.Y + exittrait.Y), 1))));
				}
				else
				{
					if (aircraftinfo.VTOL)
					{
						actor.QueueActivity(new HeliFly(actor, Target.FromCell(w, endPos)));
					}
					else
					{
						if (info.SpecialFly)
						{

							actor.QueueActivity(new ScriptedFly(actor, Target.FromCell(w, endPos)));
						}
						else
						{
							actor.QueueActivity(new Fly(actor, Target.FromCell(w, endPos)));
						}

					}
				}

				actor.QueueActivity(new RemoveSelf());
			});

			return true;
		}
		public override bool Starport(Actor self, List<ActorInfo> producees, string productionType, List<TypeDictionary> inits, ProductionQueue p)
		{
			if (IsTraitDisabled || IsTraitPaused)
				return false;

			var owner = self.Owner;
			//var production = self.Trait<ProductionQueue>
			var aircraftInfo = self.World.Map.Rules.Actors[info.ActorType].TraitInfo<AircraftInfo>();

			// WDist required to take off or land
			var landDistance = aircraftInfo.CruiseAltitude.Length * 1024 / aircraftInfo.MaximumPitch.Tan();

			// Start a fixed distance away, that corresponds to which value is picked.
			// This makes the production timing independent of spawnpoint, and instead dependent on map size.
			//Using space for starting point, it is soley dependent on values on this trait and on the ActorType.

			var startPos = self.Location + new CVec(owner.World.Map.Bounds.Width, 0);

			if (info.StartingPosition == "right")
			{
				startPos = self.Location + new CVec((int)((float)(owner.World.Map.Bounds.Width) / (float)(info.DistanceScale)), 0);
			}
			else if (info.StartingPosition == "left")
			{
				startPos = self.Location - new CVec((int)((float)owner.World.Map.Bounds.Width / (float)(info.DistanceScale)), 0);
			}
			else if (info.StartingPosition == "up")
			{
				startPos = self.Location - new CVec((int)((float)(owner.World.Map.Bounds.Height) / (float)(info.DistanceScale)), 0);
			}
			else if (info.StartingPosition == "down")
			{
				startPos = self.Location + new CVec((int)((float)(owner.World.Map.Bounds.Height) / (float)(info.DistanceScale)), 0);
			}
			else if (info.StartingPosition == "space")
			{
				startPos = self.Location;
			}

			var endPos = new CPos(owner.World.Map.Bounds.Left - 2 * landDistance / 1024, self.Location.Y);

			if (info.EndingPosition == "right")
			{
				endPos = new CPos(owner.World.Map.Bounds.Right, self.Location.Y);
			}
			if (info.EndingPosition == "left")
			{
				endPos = new CPos(owner.World.Map.Bounds.Left, self.Location.Y);
			}
			if (info.EndingPosition == "up")
			{
				endPos = new CPos(self.Location.X, owner.World.Map.Bounds.Top);
			}
			if (info.EndingPosition == "down")
			{
				endPos = new CPos(self.Location.X, owner.World.Map.Bounds.Bottom);
			}
			if (info.EndingPosition == "space")
			{
				endPos = self.Location;
			}

			// Assume a single exit point for simplicity
			var exit = self.Info.TraitInfos<ExitInfo>().First();

			foreach (var tower in self.TraitsImplementing<INotifyDelivery>())
				tower.IncomingDelivery(self);

			owner.World.AddFrameEndTask(w =>
			{
				if (!self.IsInWorld || self.IsDead)
					return;

				var exittrait = self.Info.TraitInfos<ExitInfo>().First().SpawnOffset;

				Actor actor;

				if (info.StartingPosition == "space")
				{
					actor = w.CreateActor(info.ActorType, new TypeDictionary
					{
						new CenterPositionInit(new WPos(self.CenterPosition.X + exittrait.X,self.CenterPosition.Y + exittrait.Y,aircraftInfo.CruiseAltitude.Length)),
						new OwnerInit(owner),
						new FacingInit(64)
					});
				}
				else
				{
					actor = w.CreateActor(info.ActorType, new TypeDictionary
					{
						new CenterPositionInit(w.Map.CenterOfCell(startPos) + new WVec(WDist.Zero, WDist.Zero, aircraftInfo.CruiseAltitude)),
						new OwnerInit(owner),
						new FacingInit(64)
					});
				}

				ActorInfo infotype = actor.Info;
				var aircraftinfo = infotype.TraitInfo<AircraftInfo>();

				if (info.StartingPosition == "space")
				{
					actor.QueueActivity(new HeliLand(actor, false));
				}
				else
				{
					if (aircraftinfo.VTOL)
					{
						actor.QueueActivity(new HeliFly(actor, Target.FromPos(self.CenterPosition + new WVec(landDistance, 0, 0))));
						actor.QueueActivity(new Land(actor, Target.FromActor(self), false));
					}
					else
					{
						actor.QueueActivity(new Fly(actor, Target.FromPos(self.CenterPosition + new WVec(landDistance, 0, 0))));
						actor.QueueActivity(new Land(actor, Target.FromActor(self), false));
					}
				}

				bool sound = false;

				actor.QueueActivity(new Wait(info.WaitStart + 1));

				foreach (ActorInfo somenewinfo in producees)
				{
					var someindex = producees.IndexOf(somenewinfo);
					var producee = producees.ElementAt(someindex);
					var pinit = inits.ElementAt(someindex);

					actor.QueueActivity(new Wait(info.WaitBetweenUnits + 1));
					actor.QueueActivity(new CallFunc(() =>
					{
						if (!self.IsInWorld || self.IsDead)
							return;

						foreach (var cargo in self.TraitsImplementing<INotifyDelivery>())
							cargo.Delivered(self);

						self.World.AddFrameEndTask(ww => DoProduction(self, producee, exit, productionType, pinit));
						//producees.Remove(producees.ElementAt(0));
						//inits.Remove(inits.ElementAt(0));
						if (sound == false)
						{
							sound = Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", info.ReadyAudio, self.Owner.Faction.InternalName);
						}

					}));

				}

				actor.QueueActivity(new Wait(info.WaitEnd + 1));

				if (info.EndingPosition == "space")
				{
					var move = actor.Trait<IMove>();
					actor.QueueActivity(new ScriptedTakeOff(actor, new AttackMoveActivity(actor, () => move.MoveTo(new CPos(self.CenterPosition.X + exittrait.X, self.CenterPosition.Y + exittrait.Y), 1))));
				}
				else
				{
					if (aircraftinfo.VTOL)
					{
						actor.QueueActivity(new HeliFly(actor, Target.FromCell(w, endPos)));
					}
					else
					{
						if (info.SpecialFly)
						{

							actor.QueueActivity(new ScriptedFly(actor, Target.FromCell(w, endPos)));
						}
						else
						{
							actor.QueueActivity(new Fly(actor, Target.FromCell(w, endPos)));
						}

					}
				}

				actor.QueueActivity(new RemoveSelf());
			});
			return true;
		}
	}
}
