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

using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Primitives;
using OpenRA.Mods.Common.Effects;

namespace OpenRA.Mods.Shock.Traits
{
	[Desc("If the actor is considered \"idle\", which results from various adjustable parameters, then they recieve the specified condition.")]
	class GrantConditionOnIdleInfo : ITraitInfo
	{
		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("The condition type to grant.")]
		public readonly string Condition = null;

		[Desc("How long the actor must remain \"idle\" before this condition is granted.")]
		public readonly int Duration = 0;

		[Desc("Which order strings are to also be considered idle, meaning if the actor's current order is in this list it is still considered idle" +
			"for applying this condition. Note that these are case-sensitive. Default is \"Stop\". Some examples of order strings include:" +
			"" +
			"\"Attack\", \"ForceAttack\", \"AttackMove\", \"Stop\", and \"Move\".")]
		public readonly BitSet<string> Orders = new BitSet<string>("Stop");

		[Desc("When the unit is attacked, it must wait this long to be considered idle. If this is -1, this is ignored. Default is -1.")]
		public readonly int DamagedDelay = -1;

		[Desc("Turn this on if you want \"idle attacking\" to be considered for idle status.")]
		public readonly bool IdleAttackIsIdle = false;

		[Desc("Similar to IdleAttackIsIdle, if this unit is acquiring a target than it is or is not considered Idle based on this flag." +
			"Default is false, it is not considered idle while aiming.")]
		public readonly bool AimingAllowed = false;

		[Desc("If the actor's position changes at all for any reason, such as through teleportation or general movement, than they will lose the" +
			"idle status. This is different from recieving a move order. Movement could still happen somehow without a Move or even AttackMove " +
			"order.")]
		public readonly bool AllowMovement = false;

		[Desc("If true, being repaired, or otherwise having health restored in some way, is still considered idle.")]
		public readonly bool RepairingAllowed = true;

		[Desc("If this is on, the unit is created with the idle status. Note that if anything causes them to no longer have the idle status" +
			"as normal than they will lose the condition.")]
		public readonly bool StartIdle = true;

		public object Create(ActorInitializer init) { return new GrantConditionOnIdle(init.Self, this, Duration); }
	}

	class GrantConditionOnIdle : ITick, IResolveOrder, INotifyIdle, INotifyCreated, INotifyAttack, INotifyAiming, INotifyDamage
	{
		readonly GrantConditionOnIdleInfo info;

		[Sync] int ticks;
		[Sync] int d_ticks;

		int ax;
		int ay;
		int p_ax;
		int p_ay;

		BitSet<string> orders;

		bool Is_Idle = true;
		bool attacking = false;
		bool preparing_attack = false;
		bool aiming = false;
		bool damaged = false;

		string track_order = "idle";

		ConditionManager conditionManager;
		int ConditionToken = ConditionManager.InvalidConditionToken;
		int null_token = ConditionManager.InvalidConditionToken;

		//Draws some useful text on units if this is on.
		bool debug = false;
		string debug_condition_state = "false";
		string debug_attack_state = "false";

		public GrantConditionOnIdle(Actor self, GrantConditionOnIdleInfo info, int duration)
		{
			this.info = info;
			orders = info.Orders;
			ticks = 0;
			d_ticks = 0;

			if (info.StartIdle)
			{
				Is_Idle = true;
			}
		}

		void INotifyCreated.Created(Actor self)
		{
			conditionManager = self.TraitOrDefault<ConditionManager>();

			ax = self.CenterPosition.X;
			ay = self.CenterPosition.Y;
			p_ax = ax;
			p_ay = ay;
		}

		void INotifyAttack.Attacking(Actor self, Target target, Armament a, Barrel barrel)
		{
			attacking = true;
			preparing_attack = false;
		}

		void INotifyAttack.PreparingAttack(Actor self, Target target, Armament a, Barrel barrel)
		{
			preparing_attack = true;
		}

		void INotifyAiming.StartedAiming(Actor self, AttackBase attack)
		{
			aiming = true;
		}

		void INotifyAiming.StoppedAiming(Actor self, AttackBase attack)
		{
			aiming = false;
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (!info.RepairingAllowed)
			{
				damaged = true;
				d_ticks = 0;
			}
			else if (info.RepairingAllowed && e.Damage.Value > 0)
			{
				damaged = true;
				d_ticks = 0;
			}
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			track_order = order.OrderString;

			if (!orders.Contains(order.OrderString))
			{
				Is_Idle = false;
				ticks = 0;

				if (ConditionToken != null_token)
				{ ConditionToken = conditionManager.RevokeCondition(self, ConditionToken); }
			}
			else
			{
				Is_Idle = true;
				if (info.Duration == 0 && ConditionToken == null_token)
				{
					ConditionToken = conditionManager.GrantCondition(self, info.Condition);
					ticks = 0;
				}
			}
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			if (!preparing_attack)
			{
				attacking = false;
			}

			Is_Idle = true;
			track_order = "idle";
		}

		void ITick.Tick(Actor self)
		{
			p_ax = ax;
			p_ay = ay;
			ax = self.CenterPosition.X;
			ay = self.CenterPosition.Y;

			if (damaged)
			{
				d_ticks++;
			}

			if (d_ticks >= info.DamagedDelay)
			{
				d_ticks = 0;
				damaged = false;
			}

			if ((track_order != "idle" && !orders.Contains(track_order)) || (attacking == true && info.IdleAttackIsIdle == false) ||
				(aiming == true && info.AimingAllowed == false) || (damaged && info.DamagedDelay > -1) ||
				(!info.AllowMovement && (p_ax != ax || p_ay != ay)))
			{
				Is_Idle = false;
				ticks = 0;
			}

			if (info.Duration != 0 && ConditionToken == null_token)
			{
				if (Is_Idle)
				{
					ticks++;
				}

				if (ticks >= info.Duration)
				{
					ConditionToken = conditionManager.GrantCondition(self, info.Condition);
					ticks = 0;
				}
			}

			//This is all strictly for debugging purposes. The trait does not rely on anything in this if.
			//So, if you want to remove it you can.
			if (debug)
			{
				Color c = new Color();
				c = Color.FromArgb(255, 255, 255, 255);
				WPos[] p = new WPos[4];

				p[0] = new WPos(self.CenterPosition.X, self.CenterPosition.Y + 10, self.CenterPosition.Z);
				p[1] = new WPos(self.CenterPosition.X, self.CenterPosition.Y + 300, self.CenterPosition.Z);
				p[2] = new WPos(self.CenterPosition.X, self.CenterPosition.Y + 700, self.CenterPosition.Z);
				p[3] = new WPos(self.CenterPosition.X, self.CenterPosition.Y + 1000, self.CenterPosition.Z);

				self.World.AddFrameEndTask(w => w.Add(new FloatingText(p[0], c,
					"Idle For : " + ticks.ToString(), 1)));

				self.World.AddFrameEndTask(w => w.Add(new FloatingText(p[1], c,
					"Current Order Is : " + track_order, 1)));

				if (ConditionToken != null_token)
				{
					debug_condition_state = "true";
				}
				else
				{
					debug_condition_state = "false";
				}

				if (attacking && Is_Idle)
				{
					debug_attack_state = "true";
				}
				else
				{
					debug_attack_state = "false";
				}

				self.World.AddFrameEndTask(w => w.Add(new FloatingText(p[2], c,
					"Is Condition Applied? : " + debug_condition_state, 1)));

				self.World.AddFrameEndTask(w => w.Add(new FloatingText(p[3], c,
					"Is idly attacking? : " + debug_attack_state, 1)));

			}
			//End of debug.
		}
	}
}
