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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Actor must charge up its armaments before firing.")]
	public class AttackChargesInfo : AttackOmniInfo
	{
		[Desc("Amount of charge required to attack.")]
		public readonly int ChargeLevel = 25;

		[Desc("Amount to increase the charge level each tick with a valid target.")]
		public readonly int ChargeRate = 1;

		[Desc("Amount to decrease the charge level each tick without a valid target.")]
		public readonly int DischargeRate = 1;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while the charge level is greater than zero.")]
		public readonly string ChargingCondition = null;

		public override object Create(ActorInitializer init) { return new AttackCharges(init.Self, this); }
	}

	public class AttackCharges : AttackOmni, INotifyAttack, INotifySold
	{
		readonly AttackChargesInfo info;
		ConditionManager conditionManager;
		public int chargingToken = ConditionManager.InvalidConditionToken;
		public bool charging;

		public int ChargeLevel { get; private set; }

		public AttackCharges(Actor self, AttackChargesInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		protected override void Created(Actor self)
		{
			conditionManager = self.TraitOrDefault<ConditionManager>();

			base.Created(self);
		}

		protected override void Tick(Actor self)
		{
			// Stop charging when we lose our target
			charging &= self.CurrentActivity is SetTarget;
			Charging(self);

			base.Tick(self);
		}

		protected override bool CanAttack(Actor self, Target target)
		{
			charging = base.CanAttack(self, target) && IsReachableTarget(target, true);
			return ChargeLevel >= info.ChargeLevel && charging;
		}

		protected virtual void StartCharge()
		{
			ChargeLevel = 0;
		}

		protected virtual void Selling(Actor self) { StartCharge(); }
		protected virtual void Sold(Actor self) { }

		protected virtual void Charging(Actor self)
		{
			var delta = charging ? info.ChargeRate : -info.DischargeRate;
			ChargeLevel = (ChargeLevel + delta).Clamp(0, info.ChargeLevel);

			if (ChargeLevel > 0 && conditionManager != null && !string.IsNullOrEmpty(info.ChargingCondition)
					&& chargingToken == ConditionManager.InvalidConditionToken)
				chargingToken = conditionManager.GrantCondition(self, info.ChargingCondition);

			if (ChargeLevel == 0 && conditionManager != null && chargingToken != ConditionManager.InvalidConditionToken)
				chargingToken = conditionManager.RevokeCondition(self, chargingToken);
		}

		void INotifyAttack.Attacking(Actor self, Target target, Armament a, Barrel barrel) { StartCharge(); }
		void INotifyAttack.PreparingAttack(Actor self, Target target, Armament a, Barrel barrel) { }
		void INotifySold.Selling(Actor self) { Selling(self); }
		void INotifySold.Sold(Actor self) { Sold(self); }
	}
}
