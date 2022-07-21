using System;
using System.Collections.Generic;
using System.Text;

namespace ReGitLint {
    internal static class CollectionExtensions {
        public static void AddRange<T>(this ICollection<T> source,
            IEnumerable<T> elementsToAdd) {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (elementsToAdd == null)
                throw new ArgumentNullException(nameof(elementsToAdd));

            foreach (var element in elementsToAdd) {
                source.Add(element);
            }
        }
    }
}
