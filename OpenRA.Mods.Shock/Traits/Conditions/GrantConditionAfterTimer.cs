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
using System.Drawing;
using OpenRA.Primitives;
using OpenRA.Mods.Common.Effects;

namespace OpenRA.Mods.Shock.Traits
{
	[Desc("If the actor is considered \"idle\", which results from various adjustable parameters, then they recieve the specified condition.")]
	class GrantConditionAfterTimerInfo : PausableConditionalTraitInfo, ITraitInfo
	{
		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("The condition type to grant.")]
		public readonly string Condition = null;

		[Desc("Wait this long before applying the condition.")]
		public readonly int Timer = 0;

		public override object Create(ActorInitializer init) { return new GrantConditionAfterTimer(init.Self, this); }
	}

	class GrantConditionAfterTimer : PausableConditionalTrait<GrantConditionAfterTimerInfo>, ITick, INotifyCreated
	{
		[Sync] int ticks;

		ConditionManager conditionManager;
		int ConditionToken = ConditionManager.InvalidConditionToken;
		int null_token = ConditionManager.InvalidConditionToken;

		readonly GrantConditionAfterTimerInfo info;

		public GrantConditionAfterTimer(Actor self, GrantConditionAfterTimerInfo info)
			: base(info)
		{
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			conditionManager = self.TraitOrDefault<ConditionManager>();
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitDisabled)
			{
				ticks = 0;
				if (ConditionToken != null_token)
					conditionManager.RevokeCondition(self, ConditionToken);
			}
				


			if (ConditionToken == null_token && !IsTraitPaused)
				ticks++;

			if (ticks >= info.Timer && !IsTraitPaused && !IsTraitDisabled)
			{
				ConditionToken = conditionManager.GrantCondition(self, info.Condition);
				ticks = 0;
			}
		}
	}
}