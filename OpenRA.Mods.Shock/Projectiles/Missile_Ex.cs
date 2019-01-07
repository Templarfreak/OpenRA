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
using OpenRA.Mods.Common.Effects;

namespace OpenRA.Mods.Shock.Projectiles
{
	public enum RicochetTo { Self = 0, Outer = 1, Inner = 2 }
	public enum HitGround { Explode = 0 }

	public class MissileExInfo : MissileInfo
	{
		[Desc("Subtracts from the initial horizontal launch angle to make a starting point of a range of angles to randomly pick from.")]
		public readonly WAngle RandomHLaunchAngleStart = new WAngle(256);

		[Desc("Adds to the initial horizontal launch angle to make the ending point.")]
		public readonly WAngle RandomHLaunchAngleEnd = new WAngle(256);

		[Desc("When the missile would explode it will instead wait this many ticks before doing so, freefalling while it waits.")]
		public readonly int ExplodeDelay = 0;

		[Desc("What to do when a projectile hits ground when the explosion is being delayed. Currently only accepts \"Explode.\"")]
		public readonly HitGround HitGround = HitGround.Explode;

		[Desc("When greater than 0, will \'ricochet\' to additional targets this many times.")]
		public readonly int Ricochets = 0;

		[Desc("What this missile is allouwed to \'ricochet\' to. Accepted values are Self, Outer, and Inner." +
			"Outer = targets not ricochetd to yet, Inner = targets already ricochetd to.")]
		public readonly RicochetTo[] RicochetTargets = new RicochetTo[] { RicochetTo.Outer };

		[Desc("When a missile ricochets, this controls what diplomatic stances that it can ricochet to.")]
		public readonly Stance RicochetTargetStances = Stance.Neutral | Stance.Enemy;

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
		readonly ProjectileArgs ricochetr;
		List<Target> ricochetdhits = new List<Target>();

		public MissileEx(MissileExInfo info, ProjectileArgs args)
			: base(info, args)
		{
			this.info = info;
			world = args.SourceActor.World;
			source = args.SourceActor;
			ricochetr = args;
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
				velocity = new WVec(0, -speed, 0)
					.Rotate(new WRot(WAngle.FromFacing(vFacing), WAngle.Zero, WAngle.Zero))
					.Rotate(new WRot(WAngle.Zero, WAngle.Zero, WAngle.FromFacing(hFacing)));
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

		protected void Ricochet(World world)
		{
			if (ricochetdhits.Count < info.Ricochets && info.Ricochets > 0)
			{
				ricochetdhits.Add(ricochetr.GuidedTarget);

				var range = ricochetr.Weapon.Range;
				var source = ricochetr.SourceActor;
				var allow_self = info.RicochetTargets.Contains(RicochetTo.Self);

				var targs = world.FindActorsInCircle(pos, range).Where(x =>
				(!ricochetdhits.Contains(Target.FromActor(x)) || 
				(info.RicochetTargets.Contains(RicochetTo.Inner) && ricochetdhits.Contains(Target.FromActor(x))))
				&& (x != ricochetr.SourceActor || (x == ricochetr.SourceActor && allow_self))
				&& ricochetr.Weapon.IsValidAgainst(Target.FromActor(x), world, source)
				&& info.RicochetTargetStances.HasStance(source.Owner.Stances[x.Owner]))
				.Shuffle(world.SharedRandom);

				if (targs.Count() == 0)
				{
					base.Explode(world);
					return;
				}

				var targets = targs.GetEnumerator();

				targets.MoveNext();
				var new_target = Target.FromActor(targets.Current);

				var new_args = ricochetr;
				new_args.Source = pos;
				new_args.GuidedTarget = Target.FromActor(targets.Current);
				new_args.PassiveTarget = new_args.GuidedTarget.CenterPosition;

				if (new_args.Weapon.Projectile != null)
				{
					var projectile = new MissileEx(info, new_args);
					projectile.ricochetdhits = projectile.ricochetdhits.Concat(ricochetdhits).ToList();

					if (projectile != null)
					{
						source.World.AddFrameEndTask(w => w.Add(projectile));
					}

					if (new_args.Weapon.Report != null && new_args.Weapon.Report.Any())
						Game.Sound.Play(SoundType.World, new_args.Weapon.Report.Random(source.World.SharedRandom), source.CenterPosition);
				}
			}

			base.Explode(world);
		}

		protected override WVec FreefallTick()
		{
			//Prevent missiles from going below the ground.
			var new_velocity = base.FreefallTick();
			var dist = world.Map.DistanceAboveTerrain(pos + new_velocity);

			if (exploded && info.HitGround == HitGround.Explode)
			{
				if (dist <= WDist.Zero)
				{
					new_velocity = new WVec(new_velocity.X, new_velocity.Y, 0);
				}

				if (info.ExplodeDelay > 0 && dist <= WDist.Zero)
				{
					base.Explode(world);
					return WVec.Zero;
				}
			}

			return new_velocity;
		}

		public override void Explode(World world)
		{
			exploded = true;

			if (info.ExplodeDelay > 0)
			{
				ScheduleDelayedAction(info.ExplodeDelay, () => Ricochet(world));
				return;
			}

			Ricochet(world);
		}
	}
}

