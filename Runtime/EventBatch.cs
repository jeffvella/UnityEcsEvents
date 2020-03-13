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
    internal unsafe struct EventBatch
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
            var componentTypeInfo = TypeManager.GetTypeInfo<T>();
            var componentType = ComponentType.FromTypeIndex(componentTypeInfo.TypeIndex);

            var batch = new EventBatch
            {
                Allocator = allocator,
                HasBuffer = false,

                ComponentType = componentType,
                ComponentTypeInfo = componentTypeInfo,
                ComponentTypeIndex = componentTypeInfo.TypeIndex,
                ComponentTypeSize = UnsafeUtility.SizeOf<T>(),

                ComponentQueue = new EventQueue(UnsafeUtility.SizeOf<T>(), allocator),
                BufferLinkTypeIndex = TypeManager.GetTypeIndex<BufferLink>(),

                Archetype = em.CreateArchetype(new[]
                {
                    entityComponent,
                    componentType,
                    ComponentType.ReadWrite<BufferLink>(),
                    ComponentType.ReadWrite<EventDebugInfo>()
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

                ComponentQueue = new EventQueue(UnsafeUtility.SizeOf<T1>(), allocator),
                BufferLinkTypeIndex = TypeManager.GetTypeIndex<BufferLink>(),

                Archetype = em.CreateArchetype(new[]
                {
                    entityComponent,
                    componentType,
                    bufferType,
                    ComponentType.ReadWrite<BufferLink>(),
                    ComponentType.ReadWrite<EventDebugInfo>()
                })
            };

            return batch;
        }

        //public static EventBatch Create<TComponent,TBuffer>(EntityManager em, ComponentType entityComponent, Allocator allocator) 
        //    where TComponent : struct, IComponentData
        //    where TBuffer : struct, IBufferElementData
        //{
        //    var componentTypeInfo = TypeManager.GetTypeInfo<TComponent>();
        //    var componentType = ComponentType.FromTypeIndex(componentTypeInfo.TypeIndex);

        //    var bufferTypeInfo = TypeManager.GetTypeInfo<TComponent>();
        //    var bufferType = ComponentType.FromTypeIndex(componentTypeInfo.TypeIndex);

        //    var batch = new EventBatch
        //    {
        //        ComponentTypeIndex = componentTypeInfo.TypeIndex,
        //        ComponentType = componentType,
        //        ComponentTypeSize = UnsafeUtility.SizeOf<TComponent>(),

        //        HasBufferQueue = true,
        //        BufferTypeIndex = bufferTypeInfo.TypeIndex,
        //        BufferType = bufferType,
        //        BufferTypeSize = UnsafeUtility.SizeOf<TBuffer>(),

        //        Allocator = allocator,
        //        ComponentQueue = new EventQueue(UnsafeUtility.SizeOf<TComponent>(), Allocator.Persistent),
        //        BufferQueue = new EventQueue(UnsafeUtility.SizeOf<TComponent>(), Allocator.Persistent),

        //        Archetype = em.CreateArchetype(new[]
        //        {
        //            entityComponent,
        //            componentType,
        //            bufferType
        //        })
        //    };

        //    return batch;
        //}

        internal void Dispose()
        {
            //if (IsBuffer)
            //{
            //    for (int i = -1; i < JobsUtility.MaxJobThreadCount; i++)
            //    {
            //        ref var buffer = ref ComponentQueue._bufferData.GetBuffer(i);
            //        if (buffer.Size == 0 || ComponentTypeSize == 0)
            //            continue;

            //        var length = buffer.Size / ComponentTypeSize;
            //        for (int j = 0; j < length; j++)
            //        {
            //            var header = ((BufferHeaderProxy*)buffer.Ptr)[j];
            //            UnsafeUtility.Free(header.Pointer, Allocator.TempJob);
            //        }
            //    }
            //}

            ComponentQueue.Dispose();
        }


    }

}