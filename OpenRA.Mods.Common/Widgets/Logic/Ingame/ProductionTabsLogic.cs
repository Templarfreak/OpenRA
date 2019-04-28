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
using System.Linq;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class ProductionTabsLogic : ChromeLogic
	{
		readonly ProductionTabsWidget tabs;
		readonly ProductionPaletteWidget palette;
		readonly World world;

		void SetupProductionGroupButton(ProductionTypeButtonWidget button)
		{
			if (button == null)
				return;

			Action<bool> selectTab = reverse =>
			{
				if (tabs.QueueGroup == button.ProductionGroup)
					tabs.SelectNextTab(reverse);
				else
					tabs.QueueGroup = button.ProductionGroup;

				tabs.PickUpCompletedBuilding();
			};

			button.IsDisabled = () => !tabs.Groups[button.ProductionGroup].Tabs.Any(t => t.Queue.BuildableItems().Any());
			button.OnMouseUp = mi => selectTab(mi.Modifiers.HasModifier(Modifiers.Shift));
			button.OnKeyPress = e => selectTab(e.Modifiers.HasModifier(Modifiers.Shift));
			button.IsHighlighted = () => tabs.QueueGroup == button.ProductionGroup;

			var chromeName = button.ProductionGroup.ToLowerInvariant();
			var icon = button.Get<ImageWidget>("ICON");
			icon.GetImageName = () => button.IsDisabled() ? chromeName + "-disabled" :
				tabs.Groups[button.ProductionGroup].Alert ? chromeName + "-alert" : chromeName;
		}

		[ObjectCreator.UseCtor]
		public ProductionTabsLogic(Widget widget, World world)
		{
			this.world = world;
			tabs = widget.Get<ProductionTabsWidget>("PRODUCTION_TABS");
			palette = tabs.Parent.Get<ProductionPaletteWidget>(tabs.PaletteWidget);
			world.ActorAdded += tabs.ActorChanged;
			world.ActorRemoved += tabs.ActorChanged;
			Game.BeforeGameStart += UnregisterEvents;

			var typesContainer = Ui.Root.Get(tabs.TypesContainer);
			foreach (var i in typesContainer.Children)
				SetupProductionGroupButton(i as ProductionTypeButtonWidget);

			var background = Ui.Root.GetOrNull(tabs.BackgroundContainer);
			var foreground = Ui.Root.GetOrNull(tabs.ForegroundContainer);

			if (tabs.BackgroundContainer == null) { background = null; }
			if (tabs.ForegroundContainer == null) { foreground = null; }

			if (background != null)
			{
				var palette = tabs.Parent.Get<ProductionPaletteWidget>(tabs.PaletteWidget);
				Widget background_template = null;
				Widget foreground_template = null;
				Action<int, int> updateBackground = (oldCount, newCount) => { };

				if (tabs.UseRows)
				{
					background_template = background.Get("ROW_TEMPLATE");
					var backgroundBottom = background.GetOrNull("BOTTOM_CAP");

					updateBackground = (_, icons) =>
					{
						var rows = Math.Max(palette.MinimumRows, (icons + palette.Columns - 1) / palette.Columns);
						rows = Math.Min(rows, palette.MaximumRows);

						background.RemoveChildren();

						var rowHeight = background_template.Bounds.Height;
						var rowStartY = background_template.Bounds.Y;
						for (var i = 0; i < rows; i++)
						{
							var row = background_template.Clone();
							row.Bounds.Y = (i * rowHeight) + rowStartY;
							background.AddChild(row);
						}

						if (backgroundBottom == null)
							return;

						backgroundBottom.Bounds.Y = rows * rowHeight;
						background.AddChild(backgroundBottom);

						if (foreground != null)
						{
							foreground_template = foreground.Get("ROW_TEMPLATE");

							foreground.RemoveChildren();

							rowHeight = foreground_template.Bounds.Height;
							for (var i = 0; i < rows; i++)
							{
								var row = foreground_template.Clone();
								row.Bounds.Y = i * rowHeight;
								foreground.AddChild(row);
							}
						}
					};
				}
				else
				{
					background_template = background.Get("ICON_TEMPLATE");

					updateBackground = (oldCount, newCount) =>
					{
						background.RemoveChildren();

						for (var i = 0; i < newCount; i++)
						{
							var x = i % palette.Columns;
							var y = i / palette.Columns;

							var bg = background_template.Clone();
							bg.Bounds.X = palette.IconSize.X * x;
							bg.Bounds.Y = palette.IconSize.Y * y;
							background.AddChild(bg);
						}

						if (foreground != null)
						{
							foreground_template = foreground.Get("ICON_TEMPLATE");

							for (var i = 0; i < newCount; i++)
							{
								var x = i % palette.Columns;
								var y = i / palette.Columns;

								var bg = foreground_template.Clone();
								bg.Bounds.X = palette.IconSize.X * x;
								bg.Bounds.Y = palette.IconSize.Y * y;
								background.AddChild(bg);
							}
						}
					};
				}

				palette.OnIconCountChanged += updateBackground;

				// Set the initial palette state
				updateBackground(0, 0);
			}

			// Hook up scroll up and down buttons on the palette
			var scrollDown = typesContainer.GetOrNull<ButtonWidget>("SCROLL_DOWN_BUTTON");

			if (scrollDown != null)
			{
				scrollDown.OnClick = palette.ScrollDown;
				scrollDown.IsVisible = () => palette.TotalIconCount > (palette.MaxIconRowOffset * palette.Columns);
				scrollDown.IsDisabled = () => !palette.CanScrollDown;
			}

			var scrollUp = typesContainer.GetOrNull<ButtonWidget>("SCROLL_UP_BUTTON");

			if (scrollUp != null)
			{
				scrollUp.OnClick = palette.ScrollUp;
				scrollUp.IsVisible = () => palette.TotalIconCount > (palette.MaxIconRowOffset * palette.Columns);
				scrollUp.IsDisabled = () => !palette.CanScrollUp;
			}

			SetMaximumVisibleRows(palette);
		}

		void UnregisterEvents()
		{
			Game.BeforeGameStart -= UnregisterEvents;
			world.ActorAdded -= tabs.ActorChanged;
			world.ActorRemoved -= tabs.ActorChanged;
		}

		static void SetMaximumVisibleRows(ProductionPaletteWidget productionPalette)
		{
			var screenHeight = Game.Renderer.Resolution.Height;

			// Get height of currently displayed icons
			var containerWidget = Ui.Root.GetOrNull<ContainerWidget>("SIDEBAR_PRODUCTION");

			if (containerWidget == null)
				return;

			var sidebarProductionHeight = containerWidget.Bounds.Y;

			// Check if icon heights exceed y resolution
			var maxItemsHeight = screenHeight - sidebarProductionHeight;

			var maxIconRowOffest = (maxItemsHeight / productionPalette.IconSize.Y) - 1;
			productionPalette.MaxIconRowOffset = Math.Min(maxIconRowOffest, productionPalette.MaximumRows);
		}
	}
}
