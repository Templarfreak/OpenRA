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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class FirePort
	{
		public WVec Offset;
		public WAngle Yaw;
		public WAngle Cone;
	}

	[Desc("Cargo can fire their weapons out of fire ports.")]
	public class AttackGarrisonedInfo : AttackFollowInfo, IRulesetLoaded, Requires<CargoInfo>
	{
		[FieldLoader.Require]
		[Desc("Fire port offsets in local coordinates.")]
		public readonly WVec[] PortOffsets = null;

		[FieldLoader.Require]
		[Desc("Fire port yaw angles.")]
		public readonly WAngle[] PortYaws = null;

		[FieldLoader.Require]
		[Desc("Fire port yaw cone angle.")]
		public readonly WAngle[] PortCones = null;

		[Desc("Each actor garrisoned will attempt to find their own target if the garrison actor has no direct target orders.")]
		public readonly bool PickRandomTargets = false;

		public FirePort[] Ports { get; private set; }

		[PaletteReference] public readonly string MuzzlePalette = "effect";

		public override object Create(ActorInitializer init) { return new AttackGarrisoned(init.Self, this); }
		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (PortOffsets.Length == 0)
				throw new YamlException("PortOffsets must have at least one entry.");

			if (PortYaws.Length != PortOffsets.Length)
				throw new YamlException("PortYaws must define an angle for each port.");

			if (PortCones.Length != PortOffsets.Length)
				throw new YamlException("PortCones must define an angle for each port.");

			Ports = new FirePort[PortOffsets.Length];

			for (var i = 0; i < PortOffsets.Length; i++)
			{
				Ports[i] = new FirePort
				{
					Offset = PortOffsets[i],
					Yaw = PortYaws[i],
					Cone = PortCones[i],
				};
			}

			base.RulesetLoaded(rules, ai);
		}
	}

	public class AttackGarrisoned : AttackFollow, INotifyPassengerEntered, INotifyPassengerExited, IRender
	{
		public readonly new AttackGarrisonedInfo Info;
		Lazy<BodyOrientation> coords;
		List<Armament> armaments;
		List<AnimationWithOffset> muzzles;
		Dictionary<Actor, IFacing> paxFacing;
		Dictionary<Actor, IPositionable> paxPos;
		Dictionary<Actor, RenderSprites> paxRender;
		List<Pair<int, Action>> delayedActions = new List<Pair<int, Action>>();
		bool ForcedAttack = false;

		public AttackGarrisoned(Actor self, AttackGarrisonedInfo info)
			: base(self, info)
		{
			Info = info;
			coords = Exts.Lazy(() => self.Trait<BodyOrientation>());
			armaments = new List<Armament>();
			muzzles = new List<AnimationWithOffset>();
			paxFacing = new Dictionary<Actor, IFacing>();
			paxPos = new Dictionary<Actor, IPositionable>();
			paxRender = new Dictionary<Actor, RenderSprites>();
		}

		protected override Func<IEnumerable<Armament>> InitializeGetArmaments(Actor self)
		{
			return () => armaments;
		}

		void INotifyPassengerEntered.OnPassengerEntered(Actor self, Actor passenger)
		{
			paxFacing.Add(passenger, passenger.Trait<IFacing>());
			paxPos.Add(passenger, passenger.Trait<IPositionable>());
			paxRender.Add(passenger, passenger.Trait<RenderSprites>());
			armaments.AddRange(
				passenger.TraitsImplementing<Armament>()
				.Where(a => Info.Armaments.Contains(a.Info.Name)));
		}

		void INotifyPassengerExited.OnPassengerExited(Actor self, Actor passenger)
		{
			paxFacing.Remove(passenger);
			paxPos.Remove(passenger);
			paxRender.Remove(passenger);
			armaments.RemoveAll(a => a.Actor == passenger);
		}

		FirePort SelectFirePort(Actor self, WAngle targetYaw)
		{
			// Pick a random port that faces the target
			var bodyYaw = facing != null ? WAngle.FromFacing(facing.Facing) : WAngle.Zero;
			var indices = Enumerable.Range(0, Info.Ports.Length).Shuffle(self.World.SharedRandom);
			foreach (var i in indices)
			{
				var yaw = bodyYaw + Info.Ports[i].Yaw;
				var leftTurn = (yaw - targetYaw).Angle;
				var rightTurn = (targetYaw - yaw).Angle;
				if (Math.Min(leftTurn, rightTurn) <= Info.Ports[i].Cone.Angle)
					return Info.Ports[i];
			}

			return null;
		}

		WVec PortOffset(Actor self, FirePort p)
		{
			var bodyOrientation = coords.Value.QuantizeOrientation(self, self.Orientation);
			return coords.Value.LocalToWorld(p.Offset.Rotate(bodyOrientation));
		}

		protected void ScheduleDelayedAction(int t, Action a)
		{
			if (t > 0)
				delayedActions.Add(Pair.New(t, a));
			else
				a();
		}

		public override void ResolveOrder(Actor self, Order order)
		{
			var forceAttack = order.OrderString == "ForceAttack" || order.OrderString == "Attack";
			ForcedAttack = forceAttack;
			base.ResolveOrder(self, order);
		}

		public override void DoAttack(Actor self, Target target)
		{

			if (!CanAttack(self, target))
				return;

			var pos = self.CenterPosition;

			var targetedPosition = GetTargetPosition(pos, target);
			var targetYaw = (targetedPosition - pos).Yaw;

			foreach (var a in Armaments)
			{
				var new_target = target;

				if (Info.PickRandomTargets && !ForcedAttack)
				{
					new_target = a.Actor.TraitOrDefault<AutoTarget>().ScanForTarget(a.Actor, false);

					if (new_target.Type == TargetType.Invalid)
						new_target = target;

					if (!CanAttack(self, target))
						return;

					pos = self.CenterPosition;
					targetedPosition = GetTargetPosition(pos, new_target);
					targetYaw = (targetedPosition - pos).Yaw;
				}

				var delay = a.Info.FireDelay;
				if (a.Info.RandomFireDelay > 0)
				{
					delay = self.World.SharedRandom.Next(a.Info.FireDelay, a.Info.RandomFireDelay);
				}

				ScheduleDelayedAction(delay, () =>
				{
					if (a.IsTraitDisabled || IsTraitDisabled)
						return;

					var port = SelectFirePort(self, targetYaw);
					if (port == null)
						return;

					if (!paxFacing.ContainsKey(a.Actor) || !paxPos.ContainsKey(a.Actor))
						return;

					var muzzleFacing = targetYaw.Angle / 4;
					paxFacing[a.Actor].Facing = muzzleFacing;
					paxPos[a.Actor].SetVisualPosition(a.Actor, pos + PortOffset(self, port));

					var barrel = a.CheckFire(a.Actor, facing, new_target, true);
					if (barrel == null)
						return;

					if (a.Info.MuzzleSequence != null)
					{
						// Muzzle facing is fixed once the firing starts
						var muzzleAnim = new Animation(self.World, paxRender[a.Actor].GetImage(a.Actor), () => muzzleFacing);
						var sequence = a.Info.MuzzleSequence;

						if (a.Info.MuzzleSplitFacings > 0)
							sequence += Util.QuantizeFacing(muzzleFacing, a.Info.MuzzleSplitFacings).ToString();

						var muzzleFlash = new AnimationWithOffset(muzzleAnim,
							() => PortOffset(self, port),
							() => false,
							p => RenderUtils.ZOffsetFromCenter(self, p, 1024));

						muzzles.Add(muzzleFlash);
						muzzleAnim.PlayThen(sequence, () => muzzles.Remove(muzzleFlash));
					}

					foreach (var npa in self.TraitsImplementing<INotifyAttack>())
						npa.Attacking(self, target, a, barrel);
				});
			}
		}

		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			var pal = wr.Palette(Info.MuzzlePalette);

			// Display muzzle flashes
			foreach (var m in muzzles)
				foreach (var r in m.Render(self, wr, pal, 1f))
					yield return r;
		}

		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			// Muzzle flashes don't contribute to actor bounds
			yield break;
		}

		protected override void Tick(Actor self)
		{
			for (var i = 0; i < delayedActions.Count; i++)
			{
				var x = delayedActions[i];
				if (--x.First <= 0)
					x.Second();
				delayedActions[i] = x;
			}

			delayedActions.RemoveAll(a => a.First <= 0);

			base.Tick(self);

			// Take a copy so that Tick() can remove animations
			foreach (var m in muzzles.ToArray())
				m.Animation.Tick();
		}
	}
}
