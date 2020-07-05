using Unity.Entities;

namespace Vella.Events
{
    /// <summary>
    /// This component stores extra information about how to locate the buffer associated with the main event component. 
    /// The buffers are variable length and stored sequentially by thread.
    /// </summary>
    public struct BufferLink : IComponentData
    {
        public int ThreadIndex;
        public int Offset;
        public int Length;
    }

}