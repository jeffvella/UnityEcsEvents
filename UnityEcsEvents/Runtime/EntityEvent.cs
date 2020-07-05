using Unity.Entities;

namespace Vella.Events
{
    /// <summary>
    /// A component for easy identification of events with EntityQuery
    /// </summary>
    public struct EntityEvent : IComponentData
    {
        public int Id;
        public int ComponentTypeIndex;
        public int BufferTypeIndex;
    }

}