using System.Collections;
using System.Collections.Generic;

namespace Microsoft.PowerShell.DesiredStateConfiguration
{
    /// <summary>
    /// Live, typed projection of the loosely typed property list backing a <see cref="DscResourceInfo"/>.
    /// <para>
    /// A resource exposes its properties through two shapes, and callers may mutate either one, so
    /// there is one single backing list and this view forwards every read and write to it. Handing
    /// out a converted copy instead would silently drop appends made through the other shape.
    /// </para>
    /// </summary>
    internal sealed class DscResourcePropertyInfoView(List<object> source) : IList<DscResourcePropertyInfo>
    {
        private readonly List<object> _source = source;

        public DscResourcePropertyInfo this[int index]
        {
            get => (DscResourcePropertyInfo)_source[index];
            set => _source[index] = value;
        }

        public int Count => _source.Count;

        public bool IsReadOnly => false;

        public void Add(DscResourcePropertyInfo item) => _source.Add(item);

        public void Clear() => _source.Clear();

        public bool Contains(DscResourcePropertyInfo item) => _source.Contains(item);

        public void CopyTo(DscResourcePropertyInfo[] array, int arrayIndex)
        {
            for (int i = 0; i < _source.Count; i++)
            {
                array[arrayIndex + i] = (DscResourcePropertyInfo)_source[i];
            }
        }

        public IEnumerator<DscResourcePropertyInfo> GetEnumerator()
        {
            foreach (object item in _source)
            {
                yield return (DscResourcePropertyInfo)item;
            }
        }

        public int IndexOf(DscResourcePropertyInfo item) => _source.IndexOf(item);

        public void Insert(int index, DscResourcePropertyInfo item) => _source.Insert(index, item);

        public bool Remove(DscResourcePropertyInfo item) => _source.Remove(item);

        public void RemoveAt(int index) => _source.RemoveAt(index);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
