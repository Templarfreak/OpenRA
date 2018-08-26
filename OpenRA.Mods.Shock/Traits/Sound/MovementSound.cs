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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Shock.Traits.Sound
{
	[Desc("Plays a looping audio file, but only while the actor is moving.")]
	class MovementSoundInfo : ConditionalTraitInfo
	{
		[FieldLoader.Require]
		public readonly string[] SoundFiles = null;

		[Desc("Add tags here than all units within GroupRadius that share these same tags will truncate their sounds in favor of the World actor" +
			"playing one single \"group\" sound appropriate for them. This requires the World Actor to have the GroupMovementSound trait.")]
		public readonly string[] GroupMovement = null;

		public readonly int GroupRadius = 0;

		[Desc("Initial delay (in ticks) before playing the sound for the first time.",
			"Two values indicate a random delay range.")]
		public readonly int[] Delay = { 0 };

		[Desc("Interval between playing the sound (in ticks).",
			"Two values indicate a random delay range.")]
		public readonly int[] Interval = { 0 };

		[Desc("Volume at which the sound gets played at.")]
		public readonly float Volume = 1f;

		public override object Create(ActorInitializer init) { return new MovementSound(init.Self, this); }
	}

	class MovementSound : ConditionalTrait<MovementSoundInfo>, ITick, INotifyRemovedFromWorld
	{
		GlobalMovementSound w_msound;

		readonly bool loop;
		HashSet<ISound> currentSounds = new HashSet<ISound>();
		WPos cachedPosition;
		int delay;

		int ax;
		int ay;
		int p_ax;
		int p_ay;

		bool moving = false;

		public MovementSound(Actor self, MovementSoundInfo info)
			: base(info)
		{
			var layers = self.World.WorldActor.TraitsImplementing<GlobalMovementSound>();
			w_msound = layers.First();

			if (w_msound != null)
			{
				w_msound.Groups.Add(self, info.GroupMovement.ToList());
			}


			delay = Util.RandomDelay(self.World, info.Delay);
			loop = Info.Interval.Length == 0 || (Info.Interval.Length == 1 && Info.Interval[0] == 0);
		}

		void ITick.Tick(Actor self)
		{
			p_ax = ax;
			p_ay = ay;
			ax = self.CenterPosition.X;
			ay = self.CenterPosition.Y;

			if ((p_ax != ax) || (p_ay != ay))
			{
				moving = true;
			}
			else
			{
				moving = false;
			}

			if (IsTraitDisabled)
				return;

			currentSounds.RemoveWhere(s => s == null || (!moving && s.Complete));

			var pos = self.CenterPosition;
			if (pos != cachedPosition)
			{
				foreach (var s in currentSounds)
					s.SetPosition(pos);

				cachedPosition = pos;
			}

			if (delay < 0)
				return;

			if (--delay < 0)
			{
				StartSound(self);
				if (!loop)
					delay = Util.RandomDelay(self.World, Info.Interval);
			}
		}

		void StartSound(Actor self)
		{
			var playalone = true;

			if (w_msound != null)
			{
				playalone = w_msound.PlaySingleSounds(self);
			}

			if (moving && playalone)
			{
				var sound = Info.SoundFiles.RandomOrDefault(Game.CosmeticRandom);

				ISound s;
				if (self.OccupiesSpace != null)
				{
					cachedPosition = self.CenterPosition;
					if (loop)
					{
						s = Game.Sound.PlayLooped(SoundType.World, sound, cachedPosition, Info.Volume);
					}
					else
					{
						s = Game.Sound.Play(SoundType.World, sound, self.CenterPosition, Info.Volume);
					}
				}
				else
					s = loop ? Game.Sound.PlayLooped(SoundType.World, sound, Info.Volume) :
						Game.Sound.Play(SoundType.World, sound, Info.Volume);

				currentSounds.Add(s);
			}

		}

		void StopSound()
		{
			foreach (var s in currentSounds)
				Game.Sound.StopSound(s);

			currentSounds.Clear();
		}

		protected override void TraitEnabled(Actor self) { delay = Util.RandomDelay(self.World, Info.Delay); }
		protected override void TraitDisabled(Actor self) { StopSound(); }

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { StopSound(); }
	}
}
