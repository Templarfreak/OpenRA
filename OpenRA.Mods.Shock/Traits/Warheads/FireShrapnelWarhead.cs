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
	public class FireShrapnelWarhead : WarheadAS, IRulesetLoaded<WeaponInfo>
	{
		[WeaponReference, FieldLoader.Require]
		[Desc("Has to be defined in weapons.yaml as well.")]
		public readonly string Weapon = null;

		[Desc("Amount of shrapnels thrown.")]
		public readonly int[] Amount = { 1 };

		[Desc("The chance that any particular shrapnel will had an actor.")]
		public readonly int AimChance = 0;

		[Desc("What diplomatic stances can be targeted by the shrapnel.")]
		public readonly Stance AimTargetStances = Stance.Ally | Stance.Neutral | Stance.Enemy;

		[Desc("Allow this shrapnel to be thrown randomly when no targets found.")]
		public readonly bool ThrowWithoutTarget = true;

		//[Desc("Should the shrapnel hit the direct target?")]
		//public readonly bool AllowDirectHit = false;

		[Desc("Instead of what this is normally for in WarheadAS, this is used to determine the radius from the target to search for other targets.")]
		public WDist TargetSearchRadius = WDist.FromCells(1);

		//Cluster Missile Settings

		[Desc("Percent chance that if the split missiles decide to target an actor, that they will target the original target of the original missile." +
			"Set this to 0 to emulate False of the original 'AllowDirectHit.'")]
		public readonly int RetargetAccuracy = 100;

		[Desc("Maximum amount of times the sharpnels can hit the same original target. 0 for no limit.")]
		public readonly int OriginalTargetHits = 0;

		[Desc("Maximum amount of times the sharpnels can hit any other target other than ground in particular. 0 for no limit. This is" +
			"PER TARGET. So 10 missiles and 2 targets with TargetHits of 5 has a chance for all missiles to hit somebody!")]
		public readonly int TargetHits = 0;

		[Desc("Needed to have classic Reaper-style Cluster Missiles, but to also allow for detonation-on-target-style like the original FireShrapnel by Graion." +
			" If this is off, Airburst-style *will not work, period.* The missile will detonate in the air and then no Clusters will be made because there are no ValidImpact"
			+ " targets. This makes the system not care about ValidImpact targets.")]
		public readonly bool IsAirburst = false;

		//End

		WeaponInfo weapon;

		public void RulesetLoaded(Ruleset rules, WeaponInfo info)
		{
			if (!rules.Weapons.TryGetValue(Weapon.ToLowerInvariant(), out weapon))
				throw new YamlException("Weapons Ruleset does not contain an entry '{0}'".F(Weapon.ToLowerInvariant()));
		}

		Target OriginalTarget;

		public void RememberTarg(Target ogtarg)
		{
			OriginalTarget = ogtarg;
		}

		public override void DoImpact(Target target, Target og, Actor firedBy, IEnumerable<int> damageModifiers)
		{
			var world = firedBy.World;
			var map = world.Map;
			int ogHits = 0;
			List<int> Hits = new List<int>();
			List<Actor> ActorHits = new List<Actor>();
			List<int> MaxHitCount = new List<int>();
			var rnd = new Random();
			Target loc = target;
			Target targ = og;
			Target PickedTarget;
			Target shrapnelTarget;
			int Picked;
			TargetType PickedType = TargetType.Invalid;
			int index = 1;
			Target OuterTarget;
			int Count = 0;

			var amount = Amount.Length == 2
					? world.SharedRandom.Next(Amount[0], Amount[1])
					: Amount[0];

//	This check actually makes Airburst projectiles with this Warhead impossible, which is very important for recreating the Reaper.
//	So we add the Airburst check to allow both options to be possible, but sacrifice the benefits we get from it if Airburst is on (Unsure of 
//	how much benefit it really is).

			if (IsAirburst == false)
			{
				if (!IsValidImpact(target.CenterPosition, firedBy))
					return;
			}

			var directActors = world.FindActorsInCircle(loc.CenterPosition, TargetSearchRadius).Where(x => x != og.Actor) ;

			var availableTargetActors = world.FindActorsInCircle(loc.CenterPosition, weapon.Range)
				.Where(x => directActors.Contains(x)
					&& weapon.IsValidAgainst(Target.FromActor(x), firedBy.World, firedBy)
					&& AimTargetStances.HasStance(firedBy.Owner.Stances[x.Owner]))
						.Shuffle(world.SharedRandom);

			var targetActor = availableTargetActors.GetEnumerator();
			targetActor.MoveNext();

			for (var i = 0; i < amount; i++)
			{
				if (world.SharedRandom.Next(100) <= AimChance)
				{
					shrapnelTarget = Target.FromActor(targetActor.Current);
					PickedTarget = Target.FromActor(targetActor.Current);
					//This is actually the Outer for og target
					OuterTarget = Target.FromActor(targetActor.Current);
					PickedType = TargetType.Invalid;
					if (Target.FromActor(targetActor.Current).Type != TargetType.Invalid && Target.FromActor(targetActor.Current).Type != TargetType.Terrain)
					{
						PickedType = PickedTarget.Type;
					}
					Picked = 1;
				}
				else
				{
					shrapnelTarget = target;
					PickedTarget = target;
					PickedType = target.Type;
					OuterTarget = target;
					Picked = 2;
				}

				if (PickedType != TargetType.Terrain && PickedType != TargetType.Invalid)
				{ 
					if (world.SharedRandom.Next(100) <= RetargetAccuracy)
					{ 
						shrapnelTarget = og;
						PickedTarget = og;
						PickedType = PickedTarget.Type;
						OuterTarget = Target.FromActor(targetActor.Current);
						Picked = 3;
					}
				}

				if (PickedType != TargetType.Terrain && PickedType == TargetType.Invalid && Picked == 1)
				{
					if (shrapnelTarget.Type == TargetType.Terrain || shrapnelTarget.Type == TargetType.Invalid)
					{
						shrapnelTarget = og;
						PickedTarget = og;
						//PickedType = PickedTarget.Type;
						Picked = 1;
					}
				}

				if (ogHits == OriginalTargetHits && (Picked == 3 || (PickedType == TargetType.Invalid && Picked == 1)))
				{
					shrapnelTarget = OuterTarget;
					PickedTarget = OuterTarget;
					Picked = 1;

					ActorHits.Add(PickedTarget.Actor);
					Hits.Add(1);
				}

				if (ogHits == OriginalTargetHits && Picked == 1 && PickedType == TargetType.Invalid)
				{
					shrapnelTarget = target;
					Picked = 2;
				}

				if (PickedTarget.Type != TargetType.Invalid && PickedTarget.Type != TargetType.Terrain)
				{
					if (PickedTarget.Actor == og.Actor && (ogHits < OriginalTargetHits || OriginalTargetHits == 0))
						ogHits++;
				}

				if (PickedTarget.Type != TargetType.Invalid && PickedTarget.Type != TargetType.Terrain && PickedTarget.Actor != og.Actor)
				{ 
					if (!ActorHits.Contains(PickedTarget.Actor))
						ActorHits.Add(PickedTarget.Actor);
						Hits.Add(1);
				}

				if ((PickedTarget.Type != TargetType.Invalid && PickedTarget.Type != TargetType.Terrain) && PickedTarget.Actor != og.Actor)
				{
					index = ActorHits.FindIndex(a => a == PickedTarget.Actor);

					if (ActorHits[index] == PickedTarget.Actor)
					{
						if (Hits[index] < TargetHits || TargetHits == 0)
						{
							Hits[index]++;
						}
					}
				}

				if ((PickedTarget.Type != TargetType.Invalid && PickedTarget.Type != TargetType.Terrain) && PickedTarget.Actor != og.Actor)
				{ 
					index = ActorHits.FindIndex(a => a == PickedTarget.Actor);
					if (ActorHits[index] == PickedTarget.Actor)
					{
						if (Hits[index] == TargetHits && Picked == 1 && PickedTarget.Type != TargetType.Invalid && PickedTarget.Type != TargetType.Terrain)
						{
							targetActor.MoveNext();
							//This is actually Actor Target's Outer
							OuterTarget = Target.FromActor(targetActor.Current);
							shrapnelTarget = OuterTarget;
							MaxHitCount.Add(1);
						}
						if (Hits[index] == TargetHits && Picked == 1 )
						{
							var allmaxedout = 2;
							allmaxedout = MaxHitCount.FindIndex(a => a == 0);
							if (allmaxedout == 2 && Count != amount)
							{
								shrapnelTarget = target;
								Picked = 2;
							}
						}
					}
				}

				if (ThrowWithoutTarget && (shrapnelTarget.Type == loc.Type || shrapnelTarget.Type == TargetType.Invalid))
				{
					var rotation = WRot.FromFacing(world.SharedRandom.Next(1024));
					var range = world.SharedRandom.Next(weapon.MinRange.Length, weapon.Range.Length);
					var targetpos = loc.CenterPosition + new WVec(range, 0, 0).Rotate(rotation);
					var tpos = Target.FromPos(new WPos(targetpos.X, targetpos.Y, map.CenterOfCell(map.CellContaining(targetpos)).Z));
					if (weapon.IsValidAgainst(tpos, firedBy.World, firedBy))
						shrapnelTarget = tpos;
				}

				var args = new ProjectileArgs
				{
					Weapon = weapon,
					Facing = (shrapnelTarget.CenterPosition - target.CenterPosition).Yaw.Facing,

					DamageModifiers = !firedBy.IsDead ? firedBy.TraitsImplementing<IFirepowerModifier>()
						.Select(a => a.GetFirepowerModifier()).ToArray() : new int[0],

					InaccuracyModifiers = !firedBy.IsDead ? firedBy.TraitsImplementing<IInaccuracyModifier>()
						.Select(a => a.GetInaccuracyModifier()).ToArray() : new int[0],

					RangeModifiers = !firedBy.IsDead ? firedBy.TraitsImplementing<IRangeModifier>()
						.Select(a => a.GetRangeModifier()).ToArray() : new int[0],

					Source = target.CenterPosition,
					SourceActor = firedBy,
					GuidedTarget = shrapnelTarget,
					PassiveTarget = shrapnelTarget.CenterPosition
				};

				if (args.Weapon.Projectile != null)
				{
					var projectile = args.Weapon.Projectile.Create(args);
					if (projectile != null)
					{
						firedBy.World.AddFrameEndTask(w => w.Add(projectile));
						Count++;
					}

					if (args.Weapon.Report != null && args.Weapon.Report.Any())
						Game.Sound.Play(SoundType.World, args.Weapon.Report.Random(firedBy.World.SharedRandom), target.CenterPosition);
				}
			}

		}
	}
}