using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using System;

namespace Vella.Events
{
    /// <summary>
    /// Contains all type and scheduling information for a specific event.
    /// </summary>
    public unsafe struct EventBatch
    {
        public EventQueue ComponentQueue;
        public bool HasBuffer;

        public ComponentType ComponentType;
        public TypeManager.TypeInfo ComponentTypeInfo;
        public int ComponentTypeIndex;
        public int ComponentTypeSize;

        public ComponentType BufferType;
        public TypeManager.TypeInfo BufferTypeInfo;
        public int BufferTypeIndex;
        public int BufferTypeSize;

        public EntityArchetype Archetype;
        public Allocator Allocator;

        public int BufferLinkTypeIndex;

        public static EventBatch Create<T>(EntityManager em, ComponentType entityComponent, Allocator allocator) where T : struct, IComponentData
        {
            return Create(em, entityComponent, TypeManager.GetTypeInfo<T>(), allocator);
        }

        public static EventBatch Create(EntityManager em, ComponentType entityComponent, TypeManager.TypeInfo componentTypeInfo, Allocator allocator)
        {
            var componentType = ComponentType.FromTypeIndex(componentTypeInfo.TypeIndex);

            var batch = new EventBatch
            {
                Allocator = allocator,
                HasBuffer = false,

                ComponentType = componentType,
                ComponentTypeInfo = componentTypeInfo,
                ComponentTypeIndex = componentTypeInfo.TypeIndex,
                ComponentTypeSize = componentTypeInfo.SizeInChunk,

                ComponentQueue = new EventQueue(componentTypeInfo.SizeInChunk, allocator),

                BufferLinkTypeIndex = TypeManager.GetTypeIndex<BufferLink>(),

                Archetype = em.CreateArchetype(new[]
                {
                    entityComponent,
                    componentType,
                    ComponentType.ReadWrite<BufferLink>(),
                    //ComponentType.ReadWrite<EventDebugInfo>()
                })
            };
            return batch;
        }

        internal static EventBatch Create<T1, T2>(EntityManager em, ComponentType entityComponent, Allocator allocator) 
            where T1 : struct, IComponentData 
            where T2 : struct, IBufferElementData
        {
            var componentTypeInfo = TypeManager.GetTypeInfo<T1>();
            var componentType = ComponentType.FromTypeIndex(componentTypeInfo.TypeIndex);

            var bufferTypeInfo = TypeManager.GetTypeInfo<T2>();
            var bufferType = ComponentType.FromTypeIndex(bufferTypeInfo.TypeIndex);

            var batch = new EventBatch
            {
                Allocator = allocator,
                HasBuffer = true,

                ComponentType = componentType,
                ComponentTypeInfo = componentTypeInfo,
                ComponentTypeIndex = componentTypeInfo.TypeIndex,
                ComponentTypeSize = UnsafeUtility.SizeOf<T1>(),

                BufferType = bufferType,
                BufferTypeIndex = bufferTypeInfo.TypeIndex,
                BufferTypeInfo = bufferTypeInfo,
                BufferTypeSize = UnsafeUtility.SizeOf<T2>(),

                ComponentQueue = new EventQueue(UnsafeUtility.SizeOf<T1>(), UnsafeUtility.SizeOf<T2>(), allocator),
                BufferLinkTypeIndex = TypeManager.GetTypeIndex<BufferLink>(),

                Archetype = em.CreateArchetype(new[]
                {
                    entityComponent,
                    componentType,
                    bufferType,
                    ComponentType.ReadWrite<BufferLink>(),
                    //ComponentType.ReadWrite<EventDebugInfo>()
                })
            };

            return batch;
        }

        internal void Dispose()
        {
            ComponentQueue.Dispose();
        }


    }

}