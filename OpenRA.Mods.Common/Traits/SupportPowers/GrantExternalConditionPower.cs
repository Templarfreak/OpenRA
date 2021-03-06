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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	class GrantExternalConditionPowerInfo : SupportPowerInfo
	{
		[FieldLoader.Require]
		[Desc("The condition to apply. Must be included in the target actor's ExternalConditions list.")]
		public readonly string Condition = null;

		[Desc("Duration of the condition (in ticks). Set to 0 for a permanent condition.")]
		public readonly int Duration = 0;

		[Desc("Cells - affects whole cells only")]
		public readonly int Range = 1;

		[Desc("Sound to instantly play at the targeted area.")]
		public readonly string OnFireSound = null;

		[Desc("Player stances which condition can be applied to.")]
		public readonly Stance ValidStances = Stance.Ally;

		[SequenceReference, Desc("Sequence to play for granting actor when activated.",
			"This requires the actor to have the WithSpriteBody trait or one of its derivatives.")]
		public readonly string Sequence = "active";

		[Desc("Cursor to display when there are no units to apply the condition in range.")]
		public readonly string BlockedCursor = "move-blocked";

		public override object Create(ActorInitializer init) { return new GrantExternalConditionPower(init.Self, this); }
	}

	class GrantExternalConditionPower : SupportPower
	{
		readonly GrantExternalConditionPowerInfo info;

		public GrantExternalConditionPower(Actor self, GrantExternalConditionPowerInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		public override void SelectTarget(Actor self, string order, SupportPowerManager manager)
		{
			Game.Sound.PlayToPlayer(SoundType.World, manager.Self.Owner, Info.SelectTargetSound);
			self.World.OrderGenerator = new SelectConditionTarget(Self.World, order, manager, this);
		}

		public override void Activate(Actor self, Order order, SupportPowerManager manager)
		{
			base.Activate(self, order, manager);

			var wsb = self.TraitOrDefault<WithSpriteBody>();
			if (wsb != null && wsb.DefaultAnimation.HasSequence(info.Sequence))
				wsb.PlayCustomAnimation(self, info.Sequence);

			Game.Sound.Play(SoundType.World, info.OnFireSound, order.Target.CenterPosition);

			foreach (var a in UnitsInRange(self.World.Map.CellContaining(order.Target.CenterPosition)))
			{
				var external = a.TraitsImplementing<ExternalCondition>()
					.FirstOrDefault(t => t.Info.Condition == info.Condition && t.CanGrantCondition(a, self));

				if (external != null)
					external.GrantCondition(a, self, info.Duration);
			}
		}

		public IEnumerable<Actor> UnitsInRange(CPos xy)
		{
			var range = info.Range;
			var tiles = Self.World.Map.FindTilesInCircle(xy, range);
			var units = new List<Actor>();
			foreach (var t in tiles)
				units.AddRange(Self.World.ActorMap.GetActorsAt(t));

			return units.Distinct().Where(a =>
			{
				if (!info.ValidStances.HasStance(a.Owner.Stances[Self.Owner]))
					return false;

				return a.TraitsImplementing<ExternalCondition>()
					.Any(t => t.Info.Condition == info.Condition && t.CanGrantCondition(a, Self));
			});
		}

		class SelectConditionTarget : OrderGenerator
		{
			readonly GrantExternalConditionPower power;
			readonly int range;
			readonly Sprite tile;
			readonly SupportPowerManager manager;
			readonly string order;

			public SelectConditionTarget(World world, string order, SupportPowerManager manager, GrantExternalConditionPower power)
			{
				// Clear selection if using Left-Click Orders
				if (Game.Settings.Game.UseClassicMouseStyle)
					manager.Self.World.Selection.Clear();

				this.manager = manager;
				this.order = order;
				this.power = power;
				range = power.info.Range;
				tile = world.Map.Rules.Sequences.GetSequence("overlay", "target-select").GetSprite(0);
			}

			protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
			{
				world.CancelInputMode();
				if (mi.Button == MouseButton.Left && power.UnitsInRange(cell).Any())
					yield return new Order(order, manager.Self, Target.FromCell(world, cell), false) { SuppressVisualFeedback = true };
			}

			protected override void Tick(World world)
			{
				// Cancel the og if we can't use the power
				if (!manager.Powers.ContainsKey(order))
					world.CancelInputMode();
			}

			protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world)
			{
				var xy = wr.Viewport.ViewToWorld(Viewport.LastMousePos);
				foreach (var unit in power.UnitsInRange(xy))
				{
					var bounds = unit.TraitsImplementing<IDecorationBounds>().FirstNonEmptyBounds(unit, wr);
					yield return new SelectionBoxRenderable(unit, bounds, Color.Red);
				}
			}

			protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world)
			{
				var xy = wr.Viewport.ViewToWorld(Viewport.LastMousePos);
				var pal = wr.Palette(TileSet.TerrainPaletteInternalName);

				foreach (var t in world.Map.FindTilesInCircle(xy, range))
					yield return new SpriteRenderable(tile, wr.World.Map.CenterOfCell(t), WVec.Zero, -511, pal, 1f, true);
			}

			protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
			{
				return power.UnitsInRange(cell).Any() ? power.info.Cursor : power.info.BlockedCursor;
			}
		}
	}
}
