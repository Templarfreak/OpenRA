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
		readonly Cargo[] cargos;

		public TransportProperties(ScriptContext context, Actor self)
			: base(context, self)
		{
			
			var cargos_count = self.TraitsImplementing<Cargo>().Count();
			var cargos = new Cargo[cargos_count];
			var cargosimplemnted = self.TraitsImplementing<Cargo>();

			for (var i = 0; i < cargos_count; i++)
			{
				cargos[i] = cargosimplemnted.ElementAt(i);
			}

			this.cargos = cargos;
			this.cargo = cargos.First();
		}

		[Desc("Specifies whether transport has any passengers, in any cargo hold.")]
		public bool HasAnyPassengers { get { return cargos.Any(c => c.Passengers.Any()); } }

		[Desc("Specifies whether transport has any passengers in a specific cargo hold.")]
		public bool HasPassengers(int c) { return cargos[c].Passengers.Any(); }

		[Desc("Specifies the total amount of passengers in all cargo holds.")]
		public int AllPassengerCount { get { int cnt = 0; foreach(Cargo c in cargos) { cnt += c.PassengerCount; } return cnt; } }

		[Desc("Specifies the total amount of passengers in a specific cargo hold.")]
		public int PassengerCount(int c) { return cargos[c].PassengerCount; }

		[Desc("Teleport an existing actor inside this transport's top-most cargo hold.")]
		public void LoadPassenger(Actor a) { cargos.First().Load(Self, a); }

		[Desc("Teleport an existing actor inside a specific cargo hold of this transport.")]
		public void LoadPassengerWithCargohold(Actor a, int c) { cargos[c].Load(Self, a); }

		[Desc("Remove the first actor from the transport.  This actor is not added to the world.")]
		public Actor UnloadPassenger() { return cargos.Where(c => c.PassengerCount > 0).First().Unload(Self); }

		[Desc("Remove the first actor from the specific cargo hold of the transport.  This actor is not added to the world.")]
		public Actor UnloadPassengerFromCargohold(int c) { return cargos[c].Unload(Self); }

		[Desc("Remove the specific actor from this transport.")]
		public void UnloadSpecificPassenger(Actor a) { if (cargos.Any(c => c.cargo.Contains(a)))
				cargos.First(c => c.cargo.Contains(a)).Unload(a); }

		[ScriptActorPropertyActivity]
		[Desc("Command transport to unload passengers from the specified cargo hold.")]
		public void UnloadPassengers(int c)
		{
			Self.QueueActivity(new UnloadCargo(Self, cargos[c], true));
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
