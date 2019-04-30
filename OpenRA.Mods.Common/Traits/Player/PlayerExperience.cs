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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This trait can be used to track player experience based on units killed with the `GivesExperience` trait.",
		"It can also be used as a point score system in scripted maps, for example.",
		"Attach this to the player actor.")]
	public class PlayerExperienceInfo : ITraitInfo
	{
		[Desc("The type of player experience this is.")]
		public readonly string Type = "score";

		[Desc("Maximum experience this PlayerExperience can get. Default is 32-bit Integer limit (~4 billion).")]
		public int Maximum = int.MaxValue;

		public object Create(ActorInitializer init) { return new PlayerExperience(this); }
	}

	public class PlayerExperience : ISync
	{
		[Sync] public int Experience { get; private set; }
		public PlayerExperienceInfo info;

		public PlayerExperience(PlayerExperienceInfo info)
		{
			this.info = info;
		}

		public void GiveExperience(int num)
		{
			Experience = Math.Min(Experience + num, info.Maximum);
		}

		public void SetExperience(int num)
		{
			Experience = Math.Min(num, info.Maximum);
		}
	}
}
