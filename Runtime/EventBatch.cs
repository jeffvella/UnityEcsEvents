using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Vella.Events
{
    /// <summary>
    /// Contains all type and scheduling information for a specific event component.
    /// </summary>
    internal unsafe struct EventBatch
    {
        public int TypeIndex;
        public int ComponentSize;
        public EventQueue Queue;
        public ComponentType Component;
        public EntityArchetype Archetype;
        public Allocator Allocator;

        public static EventBatch Create<T>(EntityManager em, ComponentType entityComponent, Allocator allocator) where T : struct, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var component = ComponentType.FromTypeIndex(typeIndex);

            var batch = new EventBatch
            {
                Allocator = allocator,
                TypeIndex = typeIndex,
                Component = component,
                ComponentSize = UnsafeUtility.SizeOf<T>(),
                Queue = new EventQueue(UnsafeUtility.SizeOf<T>(), Allocator.Persistent),
                Archetype = em.CreateArchetype(new[]
                {
                    entityComponent,
                    component
                })
            };

            return batch;
        }

        internal void Dispose()
        {
            Queue.Dispose();
        }
    }

}