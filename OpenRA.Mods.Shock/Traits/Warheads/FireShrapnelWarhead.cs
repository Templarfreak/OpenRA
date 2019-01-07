#region Copyright & License Information
/*
 * Copyright 2015- OpenRA.Mods.AS Developers (see AUTHORS)
 * This file is a part of a third-party plugin for OpenRA, which is
 * free software. It is made available to you under the terms of the
 * GNU General Public License as published by the Free Software
 * Foundation. For more information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.GameRules;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Shock.Warheads
{
	public enum ShrapnelType : byte { None, Outer, Terrain, Original }

	public class ShrapnelTarget
	{
		public ShrapnelType picked;
		public TargetType type;
		public Target target;
		public Target point;

		public void SetTarget(Target target)
		{
			this.target = target;
			point = Target.FromPos(target.CenterPosition);
			type = target.Actor != null ? TargetType.Actor : TargetType.Terrain;
		}
	}

	public class FireShrapnelWarhead : WarheadAS, IRulesetLoaded<WeaponInfo>, INotifyBurstComplete
	{
		[WeaponReference, FieldLoader.Require]
		[Desc("Has to be defined in weapons.yaml as well.")]
		public readonly string Weapon = null;

		[Desc("Amount of shrapnels thrown.")]
		public readonly int[] Amount = { 1 };

		[Desc("The chance that any particular shrapnel will hit an actor.")]
		public readonly int AimChance = 0;

		[Desc("What diplomatic stances can be targeted by the shrapnel.")]
		public readonly Stance AimTargetStances = Stance.Ally | Stance.Neutral | Stance.Enemy;

		[Desc("Allow this shrapnel to be thrown randomly when no targets found.")]
		public readonly bool ThrowWithoutTarget = true;

		//[Desc("Should the shrapnel hit the direct target?")]
		//public readonly bool AllowDirectHit = false;

		[Desc("Radius from the target that the main weapon detonates on to search for other targets.")]
		public WDist ShrapnelSearchRadius = WDist.FromCells(1024);

		//Cluster Missile Settings

		[Desc("Percent chance that if the split missiles decide to target an actor, that they will target the original target of the original missile." +
			"Set this to 0 to emulate False of the original 'AllowDirectHit.'")]
		public readonly int RetargetAccuracy = 100;

		[Desc("Maximum amount of times the sharpnels can hit the same original target. 0 for no limit.")]
		public readonly int OriginalTargetHits = 0;

		[Desc("Maximum amount of times the sharpnels can hit any other target other than ground in particular. 0 for no limit.")]
		public readonly int TargetHits = 0;

		[Desc("Hits for TargetHits carry over between Bursts.")]
		public readonly bool CarryOverHits = true;

		[Desc("Hits for OriginalTargetHits carry over between Bursts.")]
		public readonly bool CarryOverOriginalHits = true;

		//End

		WeaponInfo weapon;

		public void RulesetLoaded(Ruleset rules, WeaponInfo info)
		{
			if (!rules.Weapons.TryGetValue(Weapon.ToLowerInvariant(), out weapon))
				throw new YamlException("Weapons Ruleset does not contain an entry '{0}'".F(Weapon.ToLowerInvariant()));
		}

		int OGHits = 0;
		List<int> Hits = new List<int>();
		List<Actor> ActorsHit = new List<Actor>();

		void INotifyBurstComplete.FiredBurst(Actor self, Target target, Armament a)
		{
			OGHits = 0;
			Hits = new List<int>();
			ActorsHit = new List<Actor>();
		}

		public override void DoImpact(Target target, Target og, Actor firedBy, IEnumerable<int> damageModifiers)
		{
			var world = firedBy.World;
			var map = world.Map;
			int ogHits = 0;
			List<int> Hits = new List<int>();
			List<Actor> ActorHits = new List<Actor>();
			Target loc = target;
			List<ShrapnelTarget> shrapnelTargets = new List<ShrapnelTarget>();

			var amount = Amount.Length == 2
					? world.SharedRandom.Next(Amount[0], Amount[1])
					: Amount[0];

			if (!IsValidImpact(loc.CenterPosition, firedBy))
				return;

			var directActors = world.FindActorsInCircle(loc.CenterPosition, ShrapnelSearchRadius).Where(x => x != loc.Actor) ;

			var availableTargetActors = world.FindActorsInCircle(loc.CenterPosition, weapon.Range)
				.Where(x => directActors.Contains(x)
					&& weapon.IsValidAgainst(Target.FromActor(x), firedBy.World, firedBy)
					&& AimTargetStances.HasStance(firedBy.Owner.Stances[x.Owner]))
				.Shuffle(world.SharedRandom);

			var targetActor = availableTargetActors.GetEnumerator();
			targetActor.MoveNext();

			if (CarryOverHits)
			{
				ActorHits = ActorsHit;
				Hits = this.Hits;
			}

			if (CarryOverOriginalHits)
			{
				ogHits = OGHits;
			}

			for (var i = 0; i < amount; i++)
			{	
				if (ThrowWithoutTarget && (loc.Type == TargetType.Terrain || loc.Type == TargetType.Invalid || 
					loc.Type == TargetType.Actor || loc.Type == TargetType.FrozenActor))
				{
					var rotation = WRot.FromFacing(world.SharedRandom.Next(1024));
					var range = world.SharedRandom.Next(weapon.MinRange.Length, weapon.Range.Length);
					var targetpos = loc.CenterPosition + new WVec(range, 0, 0).Rotate(rotation);
					var tpos = Target.FromPos(new WPos(targetpos.X, targetpos.Y, map.CenterOfCell(map.CellContaining(targetpos)).Z));
			
					if (weapon.IsValidAgainst(tpos, firedBy.World, firedBy))
					{
						var shrapnelTarget = new ShrapnelTarget();
						shrapnelTarget.SetTarget(tpos);
						shrapnelTarget.picked = ShrapnelType.Terrain;

						shrapnelTargets.Add(shrapnelTarget);
					}
				}
			}

			foreach (ShrapnelTarget t in shrapnelTargets)
			{
				var ind = shrapnelTargets.IndexOf(t);

				if (world.SharedRandom.Next(100) <= AimChance && targetActor.Current != null)
				{
					var inx = 0;

					if (ActorHits.Contains(targetActor.Current))
					{
						inx = ActorHits.IndexOf(targetActor.Current);
						Hits[inx] += 1;
					}
					else
					{
						ActorHits.Add(targetActor.Current);
						Hits.Add(1);
						inx = ActorHits.IndexOf(targetActor.Current);
					}

					if ((Hits[inx] <= TargetHits || TargetHits == 0) && Target.FromActor(targetActor.Current).Type != TargetType.Invalid)
					{
						t.SetTarget(Target.FromActor(targetActor.Current));
						t.picked = ShrapnelType.Outer;
					}

					targetActor.MoveNext();
				}

				if (world.SharedRandom.Next(100) <= RetargetAccuracy && t.picked == ShrapnelType.Outer 
					&& (ogHits < OriginalTargetHits || OriginalTargetHits == 0) && og.Type != TargetType.Invalid)
				{
					t.SetTarget(og);
					t.picked = ShrapnelType.Original;
					ogHits += 1;
				}
			}

			if (CarryOverHits)
			{
				this.Hits = Hits;
				ActorsHit = ActorHits;
			}

			if (CarryOverOriginalHits)
			{
				OGHits = ogHits;
			}

			foreach (ShrapnelTarget t in shrapnelTargets)
			{
				Func<WPos> muzzlePosition = () => loc.CenterPosition;

				var targ = t.target;
				var targpoint = t.point;

				if ((!targ.IsValidFor(firedBy) && !targpoint.IsValidFor(firedBy)) || 
					(targ.Type == TargetType.Invalid && targpoint.Type == TargetType.Invalid))
					continue;

				if (!targ.IsValidFor(firedBy) && targpoint.IsValidFor(firedBy))
				{
					targ = targpoint;
				}
				else if (!targpoint.IsValidFor(firedBy) && targ.IsValidFor(firedBy))
				{
					targpoint = targ;
				}

				var args = new ProjectileArgs
				{
					Weapon = weapon,
					Facing = (targ.CenterPosition - loc.CenterPosition).Yaw.Facing,

					DamageModifiers = !firedBy.IsDead ? firedBy.TraitsImplementing<IFirepowerModifier>()
				.Select(a => a.GetFirepowerModifier()).ToArray() : new int[0],

					InaccuracyModifiers = !firedBy.IsDead ? firedBy.TraitsImplementing<IInaccuracyModifier>()
				.Select(a => a.GetInaccuracyModifier()).ToArray() : new int[0],

					RangeModifiers = !firedBy.IsDead ? firedBy.TraitsImplementing<IRangeModifier>()
				.Select(a => a.GetRangeModifier()).ToArray() : new int[0],

					Source = loc.CenterPosition,
					SourceActor = firedBy,
					CurrentSource = muzzlePosition,
					GuidedTarget = targ,
					PassiveTarget = targpoint.CenterPosition
				};

				if (args.Weapon.Projectile != null)
				{
					var projectile = args.Weapon.Projectile.Create(args);
					if (projectile != null)
					{
						firedBy.World.AddFrameEndTask(w => w.Add(projectile));
					}

					if (args.Weapon.Report != null && args.Weapon.Report.Any())
						Game.Sound.Play(SoundType.World, args.Weapon.Report.Random(firedBy.World.SharedRandom), loc.CenterPosition);
				}
			}
		}
	}
}