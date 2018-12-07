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

using System;
using System.Linq;
using System.Collections.Generic;
using OpenRA.Traits;
using OpenRA.Graphics;

namespace OpenRA.Mods.Common.Traits
{
	public enum TrendType : byte { Upward, Downward, Static, Random }

	[Desc("How much the unit is worth.")]
	public class ValuedInfo : ITraitInfo, IWorldTick, IRulesetLoaded
	{
		[FieldLoader.Require]
		[Desc("Used in production, but also for bounties so remember to set it > 0 even for NPCs.")]
		public readonly int Cost = 0;

		[Desc("This amount of heat gets added to this item every time it gets purchased.")]
		public readonly int Heat = 0;

		[Desc("Heat will never go over this value.")]
		public readonly int HeatMax = 0;

		[Desc("Heat will never go lower than this.")]
		public readonly int HeatMin = 0;

		[Desc("This amount of heat decays every DecayInterval. Heat will never go below 0 in this way, even if HeatMin is less than 0.")]
		public readonly int DecayAmount = 0;

		[Desc("DecayAmount gets subtracted from the current heat this often, in ticks. 0 to disable.")]
		public readonly int DecayInterval = 0;

		[Desc("Random chance that a little heat gets added. If HeatRandomAdd is 0, then Heat value is added instead.")]
		public readonly int HeatChance = 0;

		[Desc("This amount will be added when it is randomly decided to add heat. If there is more than one value here, a random value will " +
			"be picked. This also accepts negatives.")]
		public readonly int[] RandomHeat = { 0 };

		[Desc("Interval that is checked to add random heat. 0 to disable.")]
		public readonly int RandomInterval = 0;

		[Desc("The amount of heat this starts with.")]
		public readonly int StartingHeat = 0;

		[Desc("This flat amount gets added to the cost per Heat.")]
		public readonly float Inflation = 0;

		[FieldLoader.LoadUsing("LoadMinCost")]
		[Desc("The cost will never go lower than this due to inflation. If not explicitly set, defaults to Cost.")]
		public readonly int MinCost = 0;

		[FieldLoader.LoadUsing("LoadMaxCost")]
		[Desc("The cost will never go higher than this due to inflation. If not explicitly set, defaults to Cost.")]
		public readonly int MaxCost = 0;

		[Desc("Scale the build time by the total inflated cost instead of the base Cost value.")]
		public readonly bool BuildtimeScalesByInflation = false;

		[Desc("If enabled, this trait will use trends for RandomHeat. It will pick either an Upward, Downward, or Static trend, picking" +
			" a new trend every TrendInterval.")]
		public readonly bool UseTrends = false;

		[Desc("The defualt Trend type, that it starts with. Options are Upward, Downward, Static, and Random. Random will pick a random starting" +
			"trend.")]
		public readonly TrendType StartingTrend = TrendType.Static;

		[Desc("Interval to pick a new trend at. use two numbers for a random range.")]
		public readonly int[] TrendInterval = { 0 };

		[Desc("Chance to subtract during a downward trend.")]
		public readonly int DownwardChance = 0;

		[Desc("Chance to add during an upward trend.")]
		public readonly int UpwardChance = 0;

		[Desc("Chance to remain stagnent during a static trend.")]
		public readonly int StaticChance = 0;

		[Sync] public Dictionary<Player, int> heat = new Dictionary<Player, int>();
		[Sync] Dictionary<Player, int> ticks = new Dictionary<Player, int>();
		[Sync] Dictionary<Player, int> randomticks = new Dictionary<Player, int>();
		[Sync] Dictionary<Player, int> trendticks = new Dictionary<Player, int>();
		[Sync] Dictionary<Player, TrendType> currenttrend = new Dictionary<Player, TrendType>();
		List<int> DownwardTrend = new List<int>();
		List<int> UpwardTrend = new List<int>();

		static object LoadMinCost(MiniYaml yaml)
		{
			if (!yaml.Nodes.Any(n => n.Key == "MinCost"))
			{
				var cost = yaml.Nodes.Where(n => n.Key == "Cost");
				FieldLoader.Load(cost.First(), cost.First().Value);
				int new_cost;
				Int32.TryParse(cost.First().Value.Value, out new_cost);
				return new_cost;
			}
			else
			{
				var cost = yaml.Nodes.Where(n => n.Key == "MinCost");
				FieldLoader.Load(cost.First(), cost.First().Value);
				int new_cost;
				Int32.TryParse(cost.First().Value.Value, out new_cost);
				return new_cost;
			}
		}

		static object LoadMaxCost(MiniYaml yaml)
		{
			if (!yaml.Nodes.Any(n => n.Key == "MaxCost"))
			{
				var cost = yaml.Nodes.Where(n => n.Key == "Cost");
				FieldLoader.Load(cost.First(), cost.First().Value);
				int new_cost;
				Int32.TryParse(cost.First().Value.Value, out new_cost);
				return new_cost;
			}
			else
			{
				var cost = yaml.Nodes.Where(n => n.Key == "MaxCost");
				FieldLoader.Load(cost.First(), cost.First().Value);
				int new_cost;
				Int32.TryParse(cost.First().Value.Value, out new_cost);
				return new_cost;
			}
		}

		public void RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			if (TrendInterval.Count() > 1)
			{
				if (TrendInterval.Count() > 2)
				{
					throw new YamlException("TrendIntervals on {0} cannot be greater than 2!".F(info.Name));
				}

				if (TrendInterval[0] > TrendInterval[1])
				{
					throw new YamlException("TrendIntervals on {0} must be in increasing order!".F(info.Name));
				}
			}

			DownwardTrend = RandomHeat.Where(i => i <= 0).ToList();
			UpwardTrend = RandomHeat.Where(i => i >= 0).ToList();
		}

		public void Tick(World world)
		{
			foreach (var h in heat)
			{
				if (!randomticks.ContainsKey(h.Key))
				{
					randomticks.Add(h.Key, 0);
				}

				if (!ticks.ContainsKey(h.Key))
				{
					ticks.Add(h.Key, 0);
				}

				if (!trendticks.ContainsKey(h.Key))
				{
					int rand = TrendInterval[0];

					if (TrendInterval.Count() > 1)
					{
						rand = world.SharedRandom.Next(TrendInterval[0], TrendInterval[1]);
					}

					trendticks.Add(h.Key, rand);
				}

				if (!currenttrend.ContainsKey(h.Key))
				{
					if (StartingTrend == TrendType.Random)
					{
						var new_trend = world.SharedRandom.Next(0, 2);
						var trend = new_trend == 0 ? TrendType.Upward : new_trend == 1 ?
								TrendType.Downward : new_trend == 2 ? TrendType.Static : TrendType.Static;

						currenttrend.Add(h.Key, trend);
					}
					else
					{
						currenttrend.Add(h.Key, StartingTrend);
					}
					
				}
			}

			if (TrendInterval.Any(i => i > 0))
			{
				foreach (var h in heat)
				{
					trendticks[h.Key]--;

					if (trendticks[h.Key] == 0)
					{
						int rand = TrendInterval[0];

						if (TrendInterval.Count() > 1)
						{
							rand = world.SharedRandom.Next(TrendInterval[0], TrendInterval[1]);
						}

						trendticks[h.Key] = rand;
						var new_trend = world.SharedRandom.Next(0, 2);

						if (currenttrend.ContainsKey(h.Key))
						{
							currenttrend[h.Key] = new_trend == 0 ? TrendType.Upward : new_trend == 1 ?
								TrendType.Downward : new_trend == 2 ? TrendType.Static : TrendType.Static;
						}
						else
						{
							currenttrend.Add(h.Key, new_trend == 0 ? TrendType.Upward : new_trend == 1 ?
								TrendType.Downward : new_trend == 2 ? TrendType.Static : TrendType.Static);
						}


					}
				}
			}

			if (RandomInterval > 0)
			{
				foreach (var h in heat)
				{
					randomticks[h.Key]++;
				}

				foreach (var t in randomticks)
				{
					if (randomticks[t.Key] >= RandomInterval)
					{
						var random = world.SharedRandom.Next(0, 100);
						if (HeatChance > random)
						{
							var new_rand = world.SharedRandom;
							int heattoadd = 0;

							if (UseTrends)
							{
								var trend_chance = world.SharedRandom.Next(0, 100);

								if (currenttrend[t.Key] == TrendType.Upward)
								{
									if (UpwardChance > trend_chance)
									{
										heattoadd = UpwardTrend.Random(new_rand);
									}
									else
									{
										heattoadd = DownwardTrend.Random(new_rand);
									}
								}
								else if (currenttrend[t.Key] == TrendType.Downward)
								{
									if (UpwardChance > trend_chance)
									{
										heattoadd = DownwardTrend.Random(new_rand);
									}
									else
									{
										heattoadd = UpwardTrend.Random(new_rand);
									}
								}
								else if (currenttrend[t.Key] == TrendType.Static)
								{
									if (StaticChance > trend_chance)
									{
										heattoadd = 0;
									}
									else
									{
										heattoadd = RandomHeat.Random(new_rand);
									}
								}
							}
							else
							{
								heattoadd = RandomHeat.Random(new_rand);
							}

							heat[t.Key] = (heat[t.Key] + heattoadd < HeatMin) ? HeatMin : 
								(heat[t.Key] + heattoadd > HeatMax) ? HeatMax : heat[t.Key] + heattoadd;
						}
					}
				}

				foreach (var h in heat)
				{
					if (randomticks[h.Key] >= RandomInterval)
					{
						randomticks[h.Key] = 0;
					}
				}
			}

			if (DecayInterval > 0)
			{
				foreach (var h in heat)
				{
					ticks[h.Key]++;
				}

				foreach (var t in ticks)
				{
					if (ticks[t.Key] >= DecayInterval)
					{
						if (heat[t.Key] > 0)
						{
							heat[t.Key] -= DecayAmount;
						}
					}
				}

				foreach (var h in heat)
				{
					if (ticks[h.Key] >= DecayInterval)
					{
						ticks[h.Key] = 0;
					}
				}
			}
		}

		public int GetFinalCost(Player p)
		{
			if (!heat.ContainsKey(p))
			{
				heat.Add(p, StartingHeat);
			}


			var cost = Cost + (int)(heat[p] * Inflation);
			var clamp = Math.Min(cost, MaxCost);
			clamp = Math.Max(clamp, MinCost);

			return clamp;
		}

		public int GetCurrentInflation(Player p)
		{
			if (!heat.ContainsKey(p))
			{
				heat.Add(p, StartingHeat);
			}

			return (int)(heat[p] * Inflation);
		}

		public object Create(ActorInitializer init)
		{
			if (!init.Self.World.worldtick.Contains(this))
			{
				init.Self.World.Add(this);
			}

			return new Valued(init.Self, this);
		}
	}

	public class Valued
	{
		//How much this actor was actually payed for.
		public int payedfor = 0;
		Actor self;
		ValuedInfo info;

		public Valued(Actor self, ValuedInfo info)
		{
			this.self = self;
			this.info = info;

			if (!info.heat.ContainsKey(self.Owner))
			{
				info.heat.Add(self.Owner, info.StartingHeat);
			}
		}
	}
}
