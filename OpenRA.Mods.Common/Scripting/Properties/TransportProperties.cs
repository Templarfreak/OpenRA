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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Scripting;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Scripting
{
	[ScriptPropertyGroup("Transports")]
	public class TransportProperties : ScriptActorProperties, Requires<CargoInfo>
	{
		readonly Cargo cargo;

		public TransportProperties(ScriptContext context, Actor self, int whichcargo = 0)
			: base(context, self)
		{
			var cargos = self.TraitsImplementing<Cargo>();

			if (whichcargo == 0)
			{
				cargo = cargos.First();
			}
			else
			{
				cargo = cargos.ToList().ElementAt(whichcargo);
			}
		}

		[Desc("Specifies whether transport has any passengers.")]
		public bool HasPassengers { get { return cargo.Passengers.Any(); } }

		[Desc("Specifies the amount of passengers.")]
		public int PassengerCount { get { return cargo.Passengers.Count(); } }

		[Desc("Teleport an existing actor inside this transport.")]
		public void LoadPassenger(Actor a) { cargo.Load(Self, a); }

		[Desc("Remove the first actor from the transport.  This actor is not added to the world.")]
		public Actor UnloadPassenger() { return cargo.Unload(Self); }

		[ScriptActorPropertyActivity]
		[Desc("Command transport to unload passengers from the specified cargo hold.")]
		public void UnloadPassengers(Cargo cargo)
		{
			Self.QueueActivity(new UnloadCargo(Self, cargo, true));
		}

		[ScriptActorPropertyActivity]
		[Desc("Command transport to unload passengers from all cargo holds.")]
		public void UnloadAllCargoPassengers()
		{
			foreach (var cargo in Self.TraitsImplementing<Cargo>())
			{
				Self.QueueActivity(new UnloadCargo(Self, cargo, true));
			}
		}
	}
}
