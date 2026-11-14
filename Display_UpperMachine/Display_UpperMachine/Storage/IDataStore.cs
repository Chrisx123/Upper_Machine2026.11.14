using System;

namespace Display_UpperMachine.Storage
{
    /// </summary>
    public interface IDataStore : IDisposable
    {
        string Name { get; }

        void Init();

        void Save(DateTime ts, string tagName, int address, double value);
    }
}
