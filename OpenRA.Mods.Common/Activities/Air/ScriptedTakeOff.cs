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

using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class ScriptedTakeOff : Activity
	{
		const int ScanInterval = 7;

		readonly Aircraft aircraft;
		readonly IMove move;

		int scanTicks;
		Activity inner;
		AutoTarget autoTarget;

		public ScriptedTakeOff(Actor self, Activity inner)
		{
			aircraft = self.Trait<Aircraft>();
			move = self.Trait<IMove>();

			this.inner = inner;
			autoTarget = self.TraitOrDefault<AutoTarget>();
		}

		public override Activity Tick(Actor self)
		{
			if (autoTarget != null && --scanTicks <= 0)
			{
				autoTarget.ScanAndAttack(self, true);
				scanTicks = ScanInterval;
			}

			if (inner == null)
				return NextActivity;

			inner = ActivityUtils.RunActivity(self, inner);

			if (self.CenterPosition.Z == aircraft.Info.CruiseAltitude.Length)
				return NextInQueue;
			else
				return this;
		}
	}
}
