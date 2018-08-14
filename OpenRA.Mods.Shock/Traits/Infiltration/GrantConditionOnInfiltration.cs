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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using System.Drawing;
using OpenRA.Primitives;
using System;

namespace OpenRA.Mods.Shock.Traits
{
	[Desc("Grants the building a condition upon being infiltrated.")]
	class GrantConditionOnInfiltrationInfo : ITraitInfo
	{
		public readonly BitSet<TargetableType> Types;

		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("The condition type to grant.")]
		public readonly string Condition = null;

		[Desc("Length, in ticks, that the conditions lasts. If 0, it is permanent.")]
		public readonly int Duration = 0;

		[Desc("Shows a timer bar if true.")]
		public readonly bool DisplayTimer = true;

		[Desc("Color of the timer bar.")]
		public readonly Color Color = Color.White;

		public object Create(ActorInitializer init) { return new GrantConditionOnInfiltration(init.Self, this, DisplayTimer, Duration); }
	}

	class GrantConditionOnInfiltration : INotifyInfiltrated, ITick, ISelectionBar, INotifyCreated
	{
		readonly GrantConditionOnInfiltrationInfo info;
		ConditionManager conditionManager;
		int ConditionToken = ConditionManager.InvalidConditionToken;
		[Sync] int ticks;

		readonly Actor self;
		float value;
		bool timer = false;

		public GrantConditionOnInfiltration(Actor self, GrantConditionOnInfiltrationInfo info, bool timer, int duration)
		{
			this.info = info;
			this.self = self;
			this.timer = timer;
			this.ticks = 0;
		}

		void INotifyCreated.Created(Actor self)
		{
			conditionManager = self.TraitOrDefault<ConditionManager>();
		}

		//Count down ticks for the Progress Bar that is built into the trait.
		void ITick.Tick(Actor self)
		{
			if (info.Duration != 0)
			{
				if (ConditionToken != ConditionManager.InvalidConditionToken)
				{ 
					if (ticks-- < 0)
					{
						ConditionToken = conditionManager.RevokeCondition(self, ConditionToken);
						ticks = 0;
					}
					else
					{
						ticks--;
					}
				}
			}
		}

		//Do Progress Bar stuff.
		float ISelectionBar.GetValue()
		{

			if (!self.Owner.IsAlliedWith(self.World.RenderPlayer))
				return 0;

			if (info.DisplayTimer == false)
				return 0;

			if (ticks == 0)
				return 0;

			value = (float)ticks / info.Duration;

			if (ticks == 0 && value != 0) { value = 0; }

			return value;
		}

		Color ISelectionBar.GetColor() { return info.Color; }
		bool ISelectionBar.DisplayWhenEmpty { get { return false; } }

		void INotifyInfiltrated.Infiltrated(Actor self, Actor infiltrator, BitSet<TargetableType> types)
		{
			if (!info.Types.Overlaps(types))
				return;
			//Give the condition upon infiltration. Set ticks so we can remove it later and display the progress bar (if Duration > 0).
			conditionManager = self.TraitOrDefault<ConditionManager>();
			ticks = info.Duration;
			ConditionToken = conditionManager.GrantCondition(self, info.Condition);
		}
	}
}