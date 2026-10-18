// Stub of UnityEngine.Networking (Unity HLAPI / UNET) - see HlapiStub.csproj.
// Compile-time only. Signatures that the plugin really calls are marked USED.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Networking
{
    // USED: base class of RoR2.CharacterBody / CharacterMaster / Inventory /
    // PickupDropletController. Must derive from MonoBehaviour so that inherited
    // members (.gameObject, .transform, .GetComponent<T>()) resolve.
    public class NetworkBehaviour : MonoBehaviour
    {
        public NetworkInstanceId netId { get { return default(NetworkInstanceId); } }
        public bool isServer { get { return false; } }
        public bool isClient { get { return false; } }
        public bool isLocalPlayer { get { return false; } }
        public NetworkConnection connectionToServer { get { return null; } }
        public NetworkConnection connectionToClient { get { return null; } }
        public void SetDirtyBit(uint dirtyBit) { }
    }

    public class NetworkIdentity : MonoBehaviour
    {
        public NetworkInstanceId netId { get { return default(NetworkInstanceId); } }
    }

    public struct NetworkInstanceId : IEquatable<NetworkInstanceId>
    {
        public uint Value { get { return 0u; } }
        public bool IsEmpty() { return true; }
        public bool Equals(NetworkInstanceId other) { return false; }
        public override int GetHashCode() { return 0; }
        public override bool Equals(object obj) { return false; }
        public override string ToString() { return "0"; }
        public static bool operator ==(NetworkInstanceId a, NetworkInstanceId b) { return false; }
        public static bool operator !=(NetworkInstanceId a, NetworkInstanceId b) { return true; }
    }

    // USED: NetworkServer.active and NetworkServer.Spawn(GameObject)
    public class NetworkServer
    {
        public static bool active { get { return false; } }
        public static bool activeConnections { get { return false; } }
        public static int numConnections { get { return 0; } }
        public static void Spawn(GameObject obj) { }
        public static void Spawn(GameObject obj, NetworkIdentity netId) { }
        public static void Destroy(GameObject obj) { }
        public static bool SendToAll(short msgType, MessageBase msg) { return false; }
        public static void RegisterHandler(short msgType, NetworkMessageDelegate handler) { }
        public static void UnregisterHandler(short msgType) { }
    }

    public class NetworkClient
    {
        public static List<NetworkClient> allClients { get { return null; } }
        public bool active { get { return false; } }
        public void RegisterHandler(short msgType, NetworkMessageDelegate handler) { }
        public void UnregisterHandler(short msgType) { }
        public bool Send(short msgType, MessageBase msg) { return false; }
        public bool SendByChannel(short msgType, MessageBase msg, int channelId) { return false; }
    }

    public class ClientScene
    {
        public static List<PlayerController> localPlayers { get { return null; } }
        public static bool ready { get { return false; } }
    }

    public class PlayerController
    {
        public GameObject gameObject { get { return null; } }
        public NetworkIdentity playerControllerId { get { return null; } }
        public short playerControllerIndex { get { return 0; } }
    }

    public class NetworkConnection
    {
        public NetworkInstanceId playerEntityId { get { return default(NetworkInstanceId); } }
        public int connectionId { get { return 0; } }
        public string address { get { return ""; } }
        public bool SendByChannel(short msgType, MessageBase msg, int channelId) { return false; }
    }

    public class ULocalConnectionToServer : NetworkConnection { }

    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager singleton { get { return null; } }
        public NetworkClient client { get { return null; } }
    }

    public class NetworkAnimator : NetworkBehaviour { }

    // Compile-time стаб в игре не используется (Mono резолвит настоящую сборку),
    // но функциональная реализация нужна unit-тестам кодека запросов.
    public class NetworkReader
    {
        private readonly byte[] _b;
        private int _p;
        public NetworkReader() { _b = new byte[0]; }
        public NetworkReader(byte[] buffer) { _b = buffer ?? new byte[0]; }
        public byte ReadByte() { return _b[_p++]; }
        public bool ReadBoolean() { return ReadByte() != 0; }
        public int ReadInt32() { int v = System.BitConverter.ToInt32(_b, _p); _p += 4; return v; }
        public uint ReadUInt32() { uint v = System.BitConverter.ToUInt32(_b, _p); _p += 4; return v; }
        public string ReadString()
        {
            if (!ReadBoolean()) return null;
            int n = ReadInt32();
            string s = System.Text.Encoding.UTF8.GetString(_b, _p, n);
            _p += n;
            return s;
        }
    }

    public class NetworkWriter
    {
        private readonly System.IO.MemoryStream _ms = new System.IO.MemoryStream();
        public byte[] ToArray() { return _ms.ToArray(); }
        public void Write(byte value) { _ms.WriteByte(value); }
        public void Write(bool value) { _ms.WriteByte((byte)(value ? 1 : 0)); }
        public void Write(int value) { var b = System.BitConverter.GetBytes(value); _ms.Write(b, 0, 4); }
        public void Write(uint value) { var b = System.BitConverter.GetBytes(value); _ms.Write(b, 0, 4); }
        public void Write(string value)
        {
            if (value == null) { Write(false); return; }
            Write(true);
            var b = System.Text.Encoding.UTF8.GetBytes(value);
            Write(b.Length);
            _ms.Write(b, 0, b.Length);
        }
    }

    public class MessageBase
    {
        public virtual void Deserialize(NetworkReader reader) { }
        public virtual void Serialize(NetworkWriter writer) { }
    }

    public class NetworkMessage
    {
        public NetworkConnection conn;
        public NetworkReader reader;
        public short msgType;
        public T ReadMessage<T>() where T : MessageBase, new() { return new T(); }
    }

    public delegate void NetworkMessageDelegate(NetworkMessage netMsg);

    public interface INetworkTransport { }

    public class NetworkCRC
    {
        public static Dictionary<string, int> scriptCRCCheck { get { return null; } }
    }

    public static class LogFilter
    {
        public static int currentLogLevel { get; set; }
    }

    public enum PlayerSpawnMethod { Random, RoundRobin }

    public class SyncList<T> : IList<T>
    {
        public delegate void OperationCallback(int index, Operation op);
        public enum Operation { OP_ADD, OP_CLEAR, OP_INSERT, OP_REMOVE, OP_REMOVEAT, OP_SET, OP_DIRTY }
        public int Count { get { return 0; } }
        public bool IsReadOnly { get { return false; } }
        public T this[int index] { get { return default(T); } set { } }
        public void Add(T item) { }
        public void Clear() { }
        public bool Contains(T item) { return false; }
        public void CopyTo(T[] array, int arrayIndex) { }
        public IEnumerator<T> GetEnumerator() { yield break; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { yield break; }
        public int IndexOf(T item) { return -1; }
        public void Insert(int index, T item) { }
        public bool Remove(T item) { return false; }
        public void RemoveAt(int index) { }
        public void HandleMsg(NetworkMessage msg) { }
    }

    public class SyncListStruct<T> : SyncList<T> where T : struct { }
    public class SyncListInt : SyncList<int> { }
    public class SyncListUInt : SyncList<uint> { }
    public class SyncListFloat : SyncList<float> { }
    public class SyncListBool : SyncList<bool> { }
    public class SyncListString : SyncList<string> { }

    [AttributeUsage(AttributeTargets.Field)]
    public class SyncVarAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class CommandAttribute : Attribute { public int channel; }

    [AttributeUsage(AttributeTargets.Method)]
    public class ClientRpcAttribute : Attribute { public int channel; }

    [AttributeUsage(AttributeTargets.Method)]
    public class TargetRpcAttribute : Attribute { public int channel; }

    [AttributeUsage(AttributeTargets.Method)]
    public class ClientAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class ServerAttribute : Attribute { }
}
