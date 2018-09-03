#region Copyright & License Information
/*
 * Modded by Boolbada of OP Mod.
 * Started from Mod.AS's ExplodesWeapon trait but not much left.
 * 
 * Copyright 2015- OpenRA.Mods.AS Developers (see AUTHORS)
 * This file is a part of a third-party plugin for OpenRA, which is
 * free software. It is made available to you under the terms of the
 * GNU General Public License as published by the Free Software
 * Foundation. For more information, see COPYING.
 */
#endregion

using System;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Activities;
using OpenRA.GameRules;

//using OpenRA.Mods.Common.Graphics;
//using OpenRA.Effects;
//using OpenRA.Graphics;

/* Requires some base engine modifications. See the commit for more details. */

namespace OpenRA.Mods.Shock.Traits
{
	public class Forward
	{
		public int ForwardCount = 3;
		public string[] Armaments = { "primary" };
		public readonly string Condition = null;
	}

	[Desc("AttackForwarding requests other Actors with the same ForwardTypes that are available for forwarding to forward to them. Then, it can apply" +
		"conditions or change the armament based off of ChainCount. Since AttackForwarding is based off of AttackCharges, you can use ChargeLevel," +
		"ChargeRate, and DischargeRate, to determine how fast actors forward to each other.")]
	public class AttackForwardingInfo : AttackChargesInfo
	{
		[FieldLoader.LoadUsing("LoadForwards")]
		[Desc("Arrangements of Forward-types. At ChainCount, switch to Armament or apply a Condition to this actor.")]
		public List<Forward> Forwards = new List<Forward>();

		[Desc("The Forwarding types this actor is allowed to participate in. Default is Prism.")]
		public readonly string[] ForwardTypes = new string[] { "Prism" };

		[Desc("Limit of how many forwarders that can forward to this actor.")]
		public readonly int ForwardLimit = 3;

		[Desc("Slightly different from ForwardLimit, this is how many actors in total in all chains can be connected, either directly or through" +
			"other forwarders, to the main forwarder.")]
		public readonly int ForwardMaximum = 9;

		[Desc("Range that this actor can forward to, or be forwarded to by, other actors.")]
		public readonly WDist Range = new WDist(3072);

		[Desc("Can forward with any other Forwarders that share the same type and these stances. If only None, then can only Forward with self." +
			"Otherwise, None does nothing.")]
		public readonly Stance[] ForwardingStances = new Stance[] { Stance.None };

		[Desc("If this is true, then weapons with Bursts won't actually \"finish\" until the entire burst is used up, thus the network" +
			"remains active. This could theoretically keep the network active indefinitely as long as the burst never actually finishes, thus" +
			"never having to charge again.")]
		public readonly bool BurstsHangAround = false;

		[Desc("If this is true, the delay between bursts, regardless of what the weapon's actual bursts delays are, will always be at least" +
			"ChargeLevel long.")]
		public readonly bool BurstsWaitForCharge = false;

		[Desc("Muzzle position relative to turret or body, (forward, right, up) triples.")]
		public readonly WVec LocalOffset = new WVec(0,0,0);

		[Desc("Equivalent to sequence ZOffset. Controls Z sorting.")]
		public readonly int ZOffset = 0;

		[WeaponReference, FieldLoader.Require]
		[Desc("The weapon used for the effect when an actor forwards to another actor. Note that this is NOT purely visual. It is still a real" +
			"weapon. Do with that what you will. This weapon does not need to be in Armaments on this trait for it to work, though.")]
		public readonly string ForwardWeapon = null;

		static object LoadForwards(MiniYaml yaml)
		{
			var retList = new List<Forward>();
			foreach (var node in yaml.Nodes.Where(n => n.Key.StartsWith("Forward") && n.Key != "Forwards" && n.Key != "ForwardTypes" 
			&& n.Key != "ForwardLimit" && n.Key != "ForwardMaximum" && n.Key != "ForwardWeapon" && n.Key != "ForwardingStances"))
			{
				var ret = Game.CreateObject<Forward>(node.Value.Value + "Forward");
				FieldLoader.Load(ret, node.Value);
				retList.Add(ret);
			}

			return retList;
		}

		public WeaponInfo WeaponInfo { get; private set; }

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			WeaponInfo weaponInfo;

			var weaponToLower = ForwardWeapon.ToLowerInvariant();
			if (!rules.Weapons.TryGetValue(weaponToLower, out weaponInfo))
				throw new YamlException("Weapons Ruleset does not contain an entry '{0}'".F(weaponToLower));

			WeaponInfo = weaponInfo;

			if (WeaponInfo.Burst > 1 && WeaponInfo.BurstDelays.Length > 1 && (WeaponInfo.BurstDelays.Length != WeaponInfo.Burst - 1))
				throw new YamlException("Weapon '{0}' has an invalid number of BurstDelays, must be single entry or Burst - 1.".F(weaponToLower));

			base.RulesetLoaded(rules, ai);
		}

		public override object Create(ActorInitializer init) { return new AttackForwarding(init.Self, this); }
	}

	public class AttackForwarding : AttackCharges, INotifyCreated, INotifyBurstComplete //, IEffect
	{
		// Some 3rd-party mods rely on this being public
		public class Forwarding : Activity
		{
			readonly Actor Master;
			readonly AttackForwarding attack;

			public Forwarding(AttackForwarding attack, Actor Master)
			{
				this.Master = Master;
				this.attack = attack;
			}

			public override Activity Tick(Actor self)
			{
				if (IsCanceled || attack.InUse == false && attack.Master == null)
					return NextActivity;

				return this;
			}
		}

		ConditionManager conditionManager;
		List<Pair<int, string>> ConditionTokens = new List<Pair<int, string>>();
		int null_token = ConditionManager.InvalidConditionToken;

		readonly AttackForwardingInfo info;
		public Actor Self;

		//Is this "Prism Tower" forwarding its power somewhere else, or is attacking, already?
		public bool InUse = false;

		public bool ForwardingDone = true;

		public Actor Master = null;
		public AttackForwarding MasterForward = null;
		public Pair<AttackForwarding, Actor> SelfPair = new Pair<AttackForwarding, Actor>();
		public Actor DelegateForward = null;
		public WVec DelegateOffset;
		int forwardcount = 0;
		int forwardmastercount = 0;

		//List of Actors currently forwarding to this actor, and of all actors forwarding to this actor period, through all forwards
		//from all actors.
		public List<Pair<AttackForwarding, Actor>> ForwardList = new List<Pair<AttackForwarding, Actor>>();
		public List<Pair<AttackForwarding, Actor>> ForwardMasterList = new List<Pair<AttackForwarding, Actor>>();

		public List<Forward> Forwards;
		SetTarget currentactivity = null;

		WeaponInfo weap;

		public AttackForwarding(Actor self, AttackForwardingInfo info)
			: base(self, info)
		{
			Self = self;
			this.info = info;

			Forwards = info.Forwards;

			weap = info.WeaponInfo;
		}

		public List<Pair<AttackForwarding, Actor>> ViableForwards = new List<Pair<AttackForwarding, Actor>>();

		protected override void Created(Actor self)
		{
			conditionManager = self.TraitOrDefault<ConditionManager>();
			ForwardsInRange(self);
			base.Created(self);
		}

		public override void AttackTarget(Target target, bool queued, bool allowMove, bool forceAttack = false)
		{
			if (Master == Self)
			{
				currentactivity.target = target;
				return;
			}

			base.AttackTarget(target, queued, allowMove, forceAttack);
		}

		public override Activity GetAttackActivity(Actor self, Target newTarget, bool allowMove, bool forceAttack)
		{
			if (Master == null && !(charging && InUse))
			{
				currentactivity = new SetTarget(this, newTarget, allowMove);
				return currentactivity;
			}

			if (Self.CurrentActivity == currentactivity)
				return currentactivity;

			return null;
		}

		protected override void Tick(Actor self)
		{
			if (Master == Self && ForwardList.All(a => a.First.IsCharged()))
			{
				if (info.Forwards != null)
				{
					foreach (var f in info.Forwards)
					{
						if (ForwardMasterList.Count >= f.ForwardCount && f.Condition != null && 
							ConditionTokens.All(c => c.Second != f.Condition))
						{
							ConditionTokens.Add(Pair.New(conditionManager.GrantCondition(self, f.Condition), f.Condition));
						}
					}
				}

				if (currentactivity.State == ActivityState.Done && !info.BurstsHangAround)
				{
					CleanUpForwarding();
					base.StartCharge();
				}

				base.Tick(self);
			}

			if (Master != Self && IsCharged() && info.ForwardWeapon != null && ForwardingDone && DelegateForward != null)
			{
				Func<WPos> muzzlePosition = () => self.CenterPosition + info.LocalOffset;

				var args = new ProjectileArgs
				{
					Weapon = weap,
					Facing = (DelegateForward.CenterPosition - Self.CenterPosition).Yaw.Facing,

					DamageModifiers = !Self.IsDead ? Self.TraitsImplementing<IFirepowerModifier>()
					.Select(a => a.GetFirepowerModifier()).ToArray() : new int[0],

					InaccuracyModifiers = !Self.IsDead ? Self.TraitsImplementing<IInaccuracyModifier>()
					.Select(a => a.GetInaccuracyModifier()).ToArray() : new int[0],

					RangeModifiers = !Self.IsDead ? Self.TraitsImplementing<IRangeModifier>()
					.Select(a => a.GetRangeModifier()).ToArray() : new int[0],

					Source = Self.CenterPosition + info.LocalOffset,
					CurrentSource = muzzlePosition,
					SourceActor = Self,
					PassiveTarget = DelegateForward.CenterPosition + DelegateOffset,
					GuidedTarget = Target.FromPos(DelegateForward.CenterPosition + DelegateOffset)
				};

				if (args.Weapon.Projectile != null)
				{
					var projectile = args.Weapon.Projectile.Create(args);
					if (projectile != null)
						self.World.Add(projectile);
				}

				ForwardingDone = false;
			}

			if (!charging)
			{
				charging = Master != null;
			}

			if (charging && (Master == null ? false : Master != self) && ForwardList.All(a => a.First.IsCharged()))
			{
				Charging(self);
			}
		}

		public override void DoAttack(Actor self, Target target, IEnumerable<Armament> armaments = null)
		{
			if (Armaments.Any(a => a.IsReloading))
				return;

			if (!CanAttack(self, target))
				return;

			var armtofire = Armaments.First(a => a.Info.Name == "primary");

			foreach (var a in armaments ?? Armaments)
			{
				if (info.Forwards != null)
				{
					foreach (var f in info.Forwards)
					{
						if (ForwardMasterList.Count >= f.ForwardCount && f.Armaments.Any(fa => fa == a.Info.Name))
						{
							armtofire = a;
						}
					}
				}
			}

			armtofire.CheckFire(self, facing, target);
		}

		public void FiredBurst(Actor self, Target target, Armament a)
		{
			CleanUpForwarding();
			base.StartCharge();
		}

		protected override void StartCharge()
		{
			if (info.BurstsWaitForCharge)
			{
				base.StartCharge();
			}
		}

		protected override bool CanAttack(Actor self, Target target)
		{
			if (!charging && Master == null && !InUse)
			{
				if (!IsTraitPaused && !IsTraitDisabled)
				{
					ForwardList = new List<Pair<AttackForwarding, Actor>>();
					ForwardMasterList = new List<Pair<AttackForwarding, Actor>>();
					InUse = true;
					Master = Self;
					MasterForward = this;
					ForwardsInRange(Self);
					RequestForward(Self, this);
				}
			}

			var canattack = base.CanAttack(self, target);
			var charged = IsCharged();
			var forwardsready = ForwardMasterList.All(f => f.First.IsCharged());

			return canattack && charged && forwardsready;
		}

		public override void ResolveOrder(Actor self, Order order)
		{
			if (Master == Self)
			{
				if (order.OrderString != "Attack" && order.OrderString != "ForceAttack")
				{
					CleanUpForwarding();
				}
			}

			base.ResolveOrder(self, order);
		}

		public virtual void CleanUpForwarding()
		{
			foreach (var f in ForwardMasterList)
			{
				f.First.RequestDone();
			}

			List<int> tokens = new List<int>();

			foreach (var t in ConditionTokens.Where(c => c.First != null_token))
			{
				tokens.Add(t.First);
			}

			conditionManager.RevokeConditions(Self, tokens);
			ConditionTokens = new List<Pair<int, string>>();
			ForwardList = new List<Pair<AttackForwarding, Actor>>();
			ForwardMasterList = new List<Pair<AttackForwarding, Actor>>();
			charging = false;
			MasterForward = null;
			Master = null;
			InUse = false;
			base.StartCharge();
			Charging(Self);
		}

		protected void ForwardsInRange(Actor self)
		{
			ViableForwards = new List<Pair<AttackForwarding, Actor>>();

			var inRange = self.World.FindActorsInCircle(self.CenterPosition, info.Range);
			foreach (var a in inRange)
			{
				FindInRange(a, self);
			}
		}

		protected bool FindInRange(Actor ac, Actor self)
		{
			// Don't touch the same unit twice
			if (ac == self)
				return false;

			//If the actor is not a forwarder, stop
			var forward = ac.TraitOrDefault<AttackForwarding>();
			if (forward == null)
				return false;

			// Determine what stances can be forwarded with.
			if (self.Owner != forward.Self.Owner && self.Owner.Stances[forward.Self.Owner] == Stance.Ally && 
				(info.ForwardingStances.All(s => s != Stance.Ally) || forward.info.ForwardingStances.All(s => s != Stance.Ally)))
				return false;

			if (self.Owner != forward.Self.Owner && self.Owner.Stances[forward.Self.Owner] == Stance.Enemy &&
				(info.ForwardingStances.All(s => s != Stance.Enemy) || forward.info.ForwardingStances.All(s => s != Stance.Enemy)))
				return false;

			if (self.Owner != forward.Self.Owner && self.Owner.Stances[forward.Self.Owner] == Stance.Neutral &&
				(info.ForwardingStances.All(s => s != Stance.Neutral) || forward.info.ForwardingStances.All(s => s != Stance.Neutral)))
				return false;

			if ((info.ForwardingStances.All(s => s == Stance.None) && self.Owner != forward.Self.Owner) ||
				(forward.info.ForwardingStances.All(s => s == Stance.None) && self.Owner != forward.Self.Owner))
				return false;

			//If none of the ForwardTypes match, stop
			if (!forward.info.ForwardTypes.Any(f => info.ForwardTypes.Any(f2 => f == f2)))
				return false;

			//Add whoever we find to our list
			ViableForwards.Add(Pair.New(forward, forward.Self));
			return true;
		}

		protected bool IsCharged()
		{
			return ChargeLevel >= info.ChargeLevel && charging;
		}

		protected void RequestDone()
		{
			ForwardList = new List<Pair<AttackForwarding, Actor>>();
			ForwardMasterList = new List<Pair<AttackForwarding, Actor>>();
			ForwardingDone = true;
			InUse = false;
			charging = false;
			MasterForward = null;
			Master = null;
			base.StartCharge();
			Charging(Self);
		}

		protected void RequestForward(Actor master, AttackForwarding masterforward)
		{
			forwardcount = 0;
			forwardmastercount = 0;
			ForwardList = new List<Pair<AttackForwarding, Actor>>();
			ForwardMasterList = new List<Pair<AttackForwarding, Actor>>();
			ForwardsInRange(Self);

			foreach (var f in ViableForwards)
			{
				var Forward = f.First;
				var ForwardActor = f.Second;

				if (forwardcount == info.ForwardLimit)
					continue;

				if (forwardmastercount == info.ForwardMaximum)
					continue;

				if (masterforward.forwardmastercount == masterforward.info.ForwardMaximum)
					continue;

				if (Forward.InUse || !Forward.Self.IsInWorld || Forward.Master != null || Forward.Master == Forward.Self
					|| Forward.ForwardList.Count != 0 || Forward.ForwardMasterList.Count != 0 || Forward.charging || IsAiming)
					continue;

				if (Forward.Self.CurrentActivity is Forwarding || Forward.Self.CurrentActivity != null)
					continue;

				if (!Forward.charging && !Forward.IsTraitDisabled && !Forward.IsTraitPaused
						&& forwardcount < info.ForwardLimit && Forward.Self != master && !ForwardList.Contains(f))
				{
					Forward.InUse = true;
					Forward.charging = true;
					Forward.MasterForward = MasterForward;
					Forward.Master = Master;
					Forward.DelegateForward = Self;
					Forward.DelegateOffset = Forward.info.LocalOffset;
					var new_pair = Pair.New(Forward, ForwardActor);
					Forward.SelfPair = new_pair;
					ForwardList.Add(new_pair);
					ForwardMasterList.Add(Pair.New(Forward, ForwardActor));
					forwardcount += 1;
					masterforward.forwardmastercount += 1;

					Forward.Self.QueueActivity(new Forwarding(Forward, Self));
				}
			}

			foreach (var f in ForwardList)
			{
				var Forward = f.First;
				Forward.RequestForward(master, masterforward);

				foreach (var fo in Forward.ForwardMasterList)
				{
					if (Forward.forwardmastercount == masterforward.info.ForwardMaximum)
						continue;

					if (fo.Second != Self)
					{
						masterforward.ForwardMasterList.Add(fo);
					}
				}
			}
		}
	}
}