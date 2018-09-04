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
using System.Linq;
using System.Collections.Generic;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Reserve landing places for aircraft.")]
	public class ReservableInfo : ITraitInfo, IRulesetLoaded
	{
		[Desc("How many reservable spots there are for this actor.")]
		public readonly int Reservables = 1;

		[Desc("Physical WPos offsets for each reservation. The number of offsets here must be equal to Reservables, but they can all be the same.")]
		public readonly WVec[] ReserveOffsets = new WVec[] { new WVec() };

		public object Create(ActorInitializer init) { return new Reservable(init.Self, this); }

		public void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (ReserveOffsets.Length != Reservables)
			{
				throw new InvalidOperationException("ReserveOffsets on {0} must be equal to Reservables.".F(ai));
			}
		}
	}

	public class Reservable : ITick, INotifyOwnerChanged, INotifySold, INotifyActorDisposing
	{
		Actor[] reservedActors;
		Aircraft[] reservedAircrafts;
		WVec[] ReserveOffsets;

		ReservableInfo info;

		public Reservable(Actor self, ReservableInfo info)
		{
			this.info = info;
			reservedActors = new Actor[info.Reservables];
			reservedAircrafts = new Aircraft[info.Reservables];
			ReserveOffsets = new WVec[info.Reservables];
			ReserveOffsets = info.ReserveOffsets;
		}

		void ITick.Tick(Actor self)
		{
			foreach (var actor in reservedActors)
			{
				var ind = reservedActors.IndexOf(actor);

				// Nothing to do.
				if (actor == null)
					return;

				if (!Target.FromActor(actor).IsValidFor(self))
				{
					// Not likely to arrive now.
					reservedAircrafts[ind].UnReserve();
					reservedActors[ind] = null;
					reservedAircrafts[ind] = null;
				}
			}
		}

		public Pair<DisposableAction, WPos> Reserve(Actor self, Actor forActor, Aircraft forAircraft)
		{
			var index = reservedAircrafts.All(a => a != null) ? -1 : reservedAircrafts.IndexOf(reservedAircrafts.First(a => a == null));


			if (index == -1 && reservedAircrafts.Any(a => a == null))
			{
				index = reservedAircrafts.IndexOf(reservedAircrafts.First(a => a == null));
			}

			if (index == -1 && reservedAircrafts.Any(a => a == forAircraft))
			{
				index = reservedAircrafts.IndexOf(reservedAircrafts.First(a => a == forAircraft));
			}

			if (index == -1)
			{
				var has_ammo = forAircraft.self.TraitsImplementing<AmmoPool>().Count() > 0;
				var ammo = !has_ammo || forAircraft.self.TraitsImplementing<AmmoPool>().Any(am => !am.FullAmmo());
				var full_health = forAircraft.self.TraitsImplementing<IHealth>().Any(h => h.HP < h.MaxHP);

				if (reservedAircrafts.Any(ac => ac.MayYieldReservation && forAircraft.Info.DontForceYield) && ((ammo || full_health)))
				{
					index = reservedAircrafts.IndexOf(reservedAircrafts.First(a => a.MayYieldReservation));
				}
				else if (reservedAircrafts.Any(ac => ac.MayYieldReservation && !forAircraft.Info.DontForceYield))
				{
					index = reservedAircrafts.IndexOf(reservedAircrafts.First(a => a.MayYieldReservation));
				}
			}

			if (index != -1)
			{
				if (reservedAircrafts[index] != null)
				{
					reservedAircrafts[index].UnReserve();
				}

				reservedActors[index] = forActor;
				reservedAircrafts[index] = forAircraft;
			}

			if (index != -1)
			{
				if (reservedAircrafts[index] == forAircraft)
				{
					return Pair.New(new DisposableAction(
						() => { reservedActors[index] = null; reservedAircrafts[index] = null; },
						() => Game.RunAfterTick(() =>
						{
							if (Game.IsCurrentWorld(self.World))
								throw new InvalidOperationException(
									"Attempted to finalize an undisposed DisposableAction. {0} ({1}) reserved {2} ({3})".F(
									forActor.Info.Name, forActor.ActorID, self.Info.Name, self.ActorID));
						})), self.CenterPosition + ReserveOffsets[index]);
				}
			}


			return Pair.New(new DisposableAction(() => { return; }, () => { return; }), new WPos());
		}

		public static bool IsReserved(Actor a, Aircraft air = null)
		{
			var res = a.TraitOrDefault<Reservable>();
			var fully_reserved = false;

			if (res != null)
			{
				var all_reserved = res.reservedAircrafts.All(ac => ac != null);
				var self = false;

				if (air != null)
				{
					if (air.self.TraitOrDefault<AmmoPool>() != null)
					{
						var has_ammo = air.self.TraitsImplementing<AmmoPool>().Count() > 0;
						var ammo = !has_ammo || air.self.TraitsImplementing<AmmoPool>().All(am => am.FullAmmo());
						var full_health = air.self.TraitsImplementing<IHealth>().All(h => h.HP >= h.MaxHP);

						var index = -1;

						if (res.reservedAircrafts.All(ac => ac != null))
						{
							if (res.reservedAircrafts.Any(ac => ac.MayYieldReservation))
							{
								index = res.reservedAircrafts.IndexOf(res.reservedAircrafts.First(ac => ac.MayYieldReservation));
							}
						}
						

						if (index != -1)
						{
							fully_reserved = ammo && full_health && res.reservedAircrafts[index].Info.DontForceYield 
								&& all_reserved && !self;
						}
						else
						{
							self = res.reservedAircrafts.Any(ac => ac == air);
							fully_reserved = !res.reservedAircrafts.Any(ac => ac == null) && !self;
						}
					}
				}
			}

			return fully_reserved;
		}

		public static WPos FreeDock(Actor a)
		{
			var res = a.TraitOrDefault<Reservable>();
			WPos Dock = a.CenterPosition;

			if (res != null)
			{
				foreach (var reserve in res.reservedAircrafts)
				{
					var ind = res.reservedAircrafts.IndexOf(reserve);

					if (reserve == null)
					{
						Dock = Dock + res.ReserveOffsets[ind];
					}
					else if (reserve.MayYieldReservation)
					{
						Dock = Dock + res.ReserveOffsets[ind];
					}
				}
			}

			return Dock;
		}

		private void UnReserve()
		{
			foreach (var reserve in reservedAircrafts)
			{
				if (reserve != null)
					reserve.UnReserve();
			}

		}

		void INotifyActorDisposing.Disposing(Actor self) { UnReserve(); }

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { UnReserve(); }

		void INotifySold.Selling(Actor self) { UnReserve(); }
		void INotifySold.Sold(Actor self) { UnReserve(); }
	}
}
