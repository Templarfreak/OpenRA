using System;
using System.Linq;
using System.Drawing;
using OpenRA;
using System.Collections.Generic;

namespace Shock.Extensions
{
	public static class DictionaryExtensions
	{
		public static void RemoveAll<TKey, TValue>(this IDictionary<TKey, TValue> dict,
			Func<TValue, bool> predicate)
		{
			var keys = dict.Keys.Where(k => predicate(dict[k])).ToList();
			foreach (var key in keys)
			{
				dict.Remove(key);
			}
		}
	}
}