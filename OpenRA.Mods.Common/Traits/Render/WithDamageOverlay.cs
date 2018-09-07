#region Copyright & License Information
/*
 * Copyright 2007-2018 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Renders an overlay when the actor is taking heavy damage.")]
	public class WithDamageOverlayInfo : ITraitInfo, Requires<RenderSpritesInfo>
	{
		public readonly string Image = "smoke_m";

		[Desc("The palette to render this DamageOverlay in.")]
		public readonly string Palette = null;

		[SequenceReference("Image")] public readonly string IdleSequence = "idle";
		[SequenceReference("Image")] public readonly string LoopSequence = "loop";
		[SequenceReference("Image")] public readonly string EndSequence = "end";

		[Desc("Damage types that this should be used for (defined on the warheads).",
			"Leave empty to disable all filtering.")]
		public readonly BitSet<DamageType> DamageTypes = default(BitSet<DamageType>);

		[Desc("The chance that this overlay will just randomly happen again if it is in the correct damage state.")]
		public readonly int OverlayChance = 0;

		[Desc("How many ticks to wait before checking to randomly play the overlay again. ")]
		public readonly int OverlayTick = 0;

		[Desc("Trigger when Undamaged, Light, Medium, Heavy, Critical or Dead.")]
		public readonly DamageState MinimumDamageState = DamageState.Heavy;
		public readonly DamageState MaximumDamageState = DamageState.Dead;

		public object Create(ActorInitializer init) { return new WithDamageOverlay(init.Self, this); }
	}

	public class WithDamageOverlay : INotifyDamage, ITick
	{
		readonly WithDamageOverlayInfo info;
		readonly Animation anim;
		[Sync] int tick;
		[Sync] int chance;

		bool isSmoking;

		public WithDamageOverlay(Actor self, WithDamageOverlayInfo info)
		{
			this.info = info;

			var rs = self.Trait<RenderSprites>();

			anim = new Animation(self.World, info.Image);
			rs.Add(new AnimationWithOffset(anim, null, () => !isSmoking), info.Palette);
		}

		public void Tick(Actor self)
		{
			if (info.OverlayTick == 0 || info.OverlayChance == 0)
				return;

			var damage_state = Damage_State_Check(self);
			tick++;

			if (damage_state)
				return;

			if (tick >= info.OverlayTick)
			{
				chance = self.World.SharedRandom.Next(0, 100);
				tick = 0;

				if (chance > info.OverlayChance)
				{
					PlayAnim();
				}
			}

		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (!info.DamageTypes.IsEmpty && !e.Damage.DamageTypes.Overlaps(info.DamageTypes))
				return;

			var damage_state = Damage_State_Check(self, e);

			if (damage_state)
			{
				return;
			}

			PlayAnim();
		}

		bool Damage_State_Check(Actor self, AttackInfo e = null)
		{
			if (e != null)
			{
				if (isSmoking) return true; 
				if (e.Damage.Value < 0) return true;	/* getting healed */
				if (e.DamageState < info.MinimumDamageState) return true;
				if (e.DamageState > info.MaximumDamageState) return true;
			}
			else
			{
				if (isSmoking) return true;
				if (self.GetDamageState() < info.MinimumDamageState) return true;
				if (self.GetDamageState() > info.MaximumDamageState) return true;
			}

			return false;
		}

		void PlayAnim()
		{
			isSmoking = true;
			anim.PlayThen(info.IdleSequence,
				() => anim.PlayThen(info.LoopSequence,
					() => anim.PlayThen(info.EndSequence,
						() => isSmoking = false)));
		}
	}
}
