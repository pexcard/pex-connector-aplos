using System.Linq;

namespace System.Collections.Generic
{
    public static class CollectionExtensions
    {
        public static IList<T> ToShuffled<T>(this IList<T> list)
        {
            if (list is null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            var suffled = list.ToList();

            suffled.Shuffle();

            return suffled;
        }

        public static IReadOnlyList<T> ToShuffled<T>(this IReadOnlyList<T> list)
        {
            if (list is null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            var suffled = list.ToList();

            suffled.Shuffle();

            return suffled;
        }

        public static List<T> ToShuffled<T>(this List<T> list)
        {
            if (list is null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            var suffled = list.ToList();

            suffled.Shuffle();

            return suffled;
        }

        public static void Shuffle<T>(this IList<T> list)
        {
            if (list is null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            var n = list.Count;
            while (n > 1)
            {
                n--;

                var k = Random.Shared.Next(n + 1);
                var value = list[k];

                list[k] = list[n];
                list[n] = value;
            }
        }

        public static ICollection<T> RemoveWhere<T>(this ICollection<T> collection, Predicate<T> predicate)
        {
            if (collection is null)
            {
                throw new ArgumentNullException(nameof(collection));
            }
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            foreach (var item in collection.ToList())
            {
                if (predicate(item))
                {
                    collection.Remove(item);
                }
            }

            return collection;
        }

        public static ICollection<T> RemoveItems<T>(this ICollection<T> collection, IEnumerable<T> items)
        {
            if (collection is null)
            {
                throw new ArgumentNullException(nameof(collection));
            }
            if (items is null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            foreach (var item in items.ToList())
            {
                collection.Remove(item);
            }

            return collection;
        }
    }
}
