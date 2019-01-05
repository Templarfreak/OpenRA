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
using OpenRA.Mods.Common.Projectiles;
using OpenRA.GameRules;
using OpenRA.Traits;
using OpenRA.Mods.Common;

namespace OpenRA.Mods.Shock.Projectiles
{
	public enum BouncesTo : byte { Self, Outer, Inner }

	public class MissileExInfo : MissileInfo
	{
		[Desc("Subtracts from the initial horizontal launch angle to make a starting point of a range of angles to randomly pick from.")]
		public readonly WAngle RandomHLaunchAngleStart = new WAngle(256);

		[Desc("Adds to the initial horizontal launch angle to make the ending point.")]
		public readonly WAngle RandomHLaunchAngleEnd = new WAngle(256);

		[Desc("When the missile would explode it will instead wait this many ticks before doing so, freefalling while it waits.")]
		public readonly int ExplodeDelay = 0;

		[Desc("When greater than 0, will \'bounce\' to additional targets this many times.")]
		public readonly int Bounces = 0;

		[Desc("What this missile is allouwed to \'bounce\' to. Accepted values are Self, Outer, and Inner." +
			"Outer = targets not bounced to yet, Inner = targets already bounced to.")]
		public readonly BouncesTo[] BounceTargets = new BouncesTo[] { BouncesTo.Outer };

		[Desc("What diplomatic stances can be targeted by the shrapnel.")]
		public readonly Stance BounceTargetStances = Stance.Neutral | Stance.Enemy;

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
		readonly ProjectileArgs bouncer;
		List<Target> bouncedhits = new List<Target>();

		public MissileEx(MissileExInfo info, ProjectileArgs args)
			: base(info, args)
		{
			this.info = info;
			world = args.SourceActor.World;
			source = args.SourceActor;
			bouncer = args;
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

		//This is WIP and is not complete.
		//protected void Bounce(World world)
		//{
		//	if (bouncedhits.Count < info.Bounces && info.Bounces > 0)
		//	{
		//		bouncedhits.Add(bouncer.GuidedTarget);
		//
		//		var range = bouncer.Weapon.Range;
		//		var source = bouncer.SourceActor;
		//		var allow_self = info.BounceTargets.Contains(BouncesTo.Self);
		//
		//		var targs = world.FindActorsInCircle(pos, range).Where(x =>
		//		(!bouncedhits.Contains(Target.FromActor(x)) ||
		//		(info.BounceTargets.Contains(BouncesTo.Inner) && bouncedhits.Contains(Target.FromActor(x))))
		//		&& (x != bouncer.SourceActor || (x == bouncer.SourceActor && allow_self))
		//		&& bouncer.Weapon.IsValidAgainst(Target.FromActor(x), world, source)
		//		&& info.BounceTargetStances.HasStance(source.Owner.Stances[x.Owner]))
		//		.Shuffle(world.SharedRandom);
		//
		//		if (targs.Count() == 0)
		//		{
		//			base.Explode(world);
		//			return;
		//		}
		//
		//		var targets = targs.GetEnumerator();
		//
		//		targets.MoveNext();
		//		var new_target = Target.FromActor(targets.Current);
		//
		//		var new_args = bouncer;
		//		new_args.GuidedTarget = Target.FromActor(targets.Current);
		//		new_args.PassiveTarget = new_args.GuidedTarget.CenterPosition;
		//
		//		if (new_args.Weapon.Projectile != null)
		//		{
		//			var projectile = new MissileEx(info, new_args);
		//			projectile.bouncedhits.Union(bouncedhits);
		//
		//			if (projectile != null)
		//			{
		//				source.World.AddFrameEndTask(w => w.Add(projectile));
		//			}
		//
		//			if (new_args.Weapon.Report != null && new_args.Weapon.Report.Any())
		//				Game.Sound.Play(SoundType.World, new_args.Weapon.Report.Random(source.World.SharedRandom), source.CenterPosition);
		//		}
		//	}
		//
		//	base.Explode(world);
		//}

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

