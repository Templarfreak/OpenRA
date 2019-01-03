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
using OpenRA.Primitives;
using OpenRA.Mods.Common.Projectiles;
using OpenRA.GameRules;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Shock.Projectiles
{
	public class MissileExInfo : MissileInfo
	{
		[Desc("Subtracts from the initial horizontal launch angle to make a starting point of a range of angles to randomly pick from.")]
		public readonly WAngle RandomHLaunchAngleStart = new WAngle(256);

		[Desc("Adds to the initial horizontal launch angle to make the ending point.")]
		public readonly WAngle RandomHLaunchAngleEnd = new WAngle(256);

		[Desc("When the missile would explode it will instead wait this many ticks before doing so, freefalling while it waits.")]
		public readonly int ExplodeDelay = 0;

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
		List<Pair<int, Action>> delayedActions = new List<Pair<int, Action>>();
		MissileExInfo info;
		bool exploded = false;
		readonly World world;
		readonly Actor source;

		public MissileEx(MissileExInfo info, ProjectileArgs args)
			: base(info, args)
		{
			this.info = info;
			world = args.SourceActor.World;
			source = args.SourceActor;
		}

		public override void Tick(World world)
		{

			for (var i = 0; i < delayedActions.Count; i++)
			{
				var x = delayedActions[i];
				if (--x.First <= 0)
					x.Second();
				delayedActions[i] = x;
			}

			delayedActions.RemoveAll(a => a.First <= 0);

			if (exploded)
			{
				state = States.Freefall;
			}

			base.Tick(world);
		}

		protected void ScheduleDelayedAction(int t, Action a)
		{
			if (t > 0)
				delayedActions.Add(Pair.New(t, a));
			else
				a();
		}

		public override void Explode(World world)
		{
			exploded = true;

			if (info.ExplodeDelay > 0)
			{
				ScheduleDelayedAction(info.ExplodeDelay, () => base.Explode(world));
				return;
			}

			base.Explode(world);
		}
	}
}

