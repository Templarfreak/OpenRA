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

using OpenRA.Mods.Common.Projectiles;
using OpenRA.GameRules;

namespace OpenRA.Mods.Shock.Projectiles
{
	public class MissileExInfo : MissileInfo
	{
		[Desc("Subtracts from the initial horizontal launch angle to make a starting point of a range of angles to randomly pick from.")]
		public readonly WAngle RandomHLaunchAngleStart = new WAngle(256);

		[Desc("Adds to the initial horizontal launch angle to make the ending point.")]
		public readonly WAngle RandomHLaunchAngleEnd = new WAngle(256);

		public override IProjectile Create(ProjectileArgs args)
		{
			var world = args.SourceActor.World;

			var start = args.Facing - RandomHLaunchAngleStart.Angle;
			var end = args.Facing + RandomHLaunchAngleEnd.Angle;

			var new_angle = new WAngle(world.SharedRandom.Next(start, end));

			var new_facing = args.Facing + new_angle.Angle;
			args.Facing = new_facing;

			return new MissileEx(this, args);
		}
	}

	public class MissileEx : Missile
	{
		public MissileEx(MissileExInfo info, ProjectileArgs args)
			: base(info, args) {}
	}
}

