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

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Scripting;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Scripting
{
	[ScriptPropertyGroup("Player")]
	public class PlayerExperienceProperties : ScriptPlayerProperties, Requires<PlayerExperienceInfo>
	{
		readonly PlayerExperience[] exp;

		public PlayerExperienceProperties(ScriptContext context, Player player)
			: base(context, player)
		{
			exp = player.PlayerActor.TraitsImplementing<PlayerExperience>().ToArray();
		}

		public int GetSpecificExperience(int e)
		{
			return exp[e].Experience;
		}

		public void SetSpecificExperience(int e, int ex)
		{
			exp[e].SetExperience(ex);
		}

		public void ModifyExperience(int e, int ex)
		{
			exp[e].GiveExperience(ex);
		}

		public int Experience
		{
			get
			{
				return exp.First().Experience;
			}

			set
			{
				exp.First().SetExperience(value);
			}
		}
	}
}
