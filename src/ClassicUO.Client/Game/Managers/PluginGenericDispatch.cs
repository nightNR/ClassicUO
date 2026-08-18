// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.PluginAbi;
using ClassicUO.Game;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Handles <see cref="ClientBindings.GenericInvokeFn"/> calls from a v2
    /// plugin/host. New capabilities are added as new opcode cases here
    /// instead of new <see cref="ClientBindings"/> fields — see the type doc
    /// on <see cref="GenericOpcodes"/>.
    /// </summary>
    internal static unsafe class PluginGenericDispatch
    {
        public static int Invoke(int opcode, IntPtr argsPtr, int argsLen, IntPtr outBuf, int outCap)
        {
            switch (opcode)
            {
                case GenericOpcodes.GetSupportedOpcodes:
                    return WriteSupportedOpcodes(outBuf, outCap);

                case GenericOpcodes.ListOpenStatusBars:
                    return PluginStatusBarEvents.ListOpen(outBuf, outCap);

                case GenericOpcodes.Attack:
                    return InvokeAttack(argsPtr, argsLen);

                case GenericOpcodes.SetNameplateOverride:
                    return InvokeSetNameplateOverride(argsPtr, argsLen);

                case GenericOpcodes.ClearNameplateOverride:
                    return InvokeClearNameplateOverride(argsPtr, argsLen);

                case GenericOpcodes.ClearAllNameplateOverrides:
                    return InvokeClearAllNameplateOverrides();

                default:
                    return -1; // unknown opcode
            }
        }

        private static int InvokeAttack(IntPtr argsPtr, int argsLen)
        {
            if (argsLen < sizeof(uint))
                return -2; // args buffer too small

            uint serial = *(uint*)argsPtr;

            World world = Client.Game?.UO?.World;
            if (world == null)
                return -1;

            GameActions.Attack(world, serial);
            return 0;
        }

        private static int InvokeSetNameplateOverride(IntPtr argsPtr, int argsLen)
        {
            if (argsLen < sizeof(NameplateOverridePayload))
                return -2; // args buffer too small

            var payload = *(NameplateOverridePayload*)argsPtr;

            World world = Client.Game?.UO?.World;
            if (world == null)
                return -1;

            world.NameOverHeadManager.SetOverride(payload.Serial, payload.Visible != 0, payload.OpacityPercent);
            return 0;
        }

        private static int InvokeClearNameplateOverride(IntPtr argsPtr, int argsLen)
        {
            if (argsLen < sizeof(uint))
                return -2; // args buffer too small

            uint serial = *(uint*)argsPtr;

            World world = Client.Game?.UO?.World;
            if (world == null)
                return -1;

            world.NameOverHeadManager.ClearOverride(serial);
            return 0;
        }

        private static int InvokeClearAllNameplateOverrides()
        {
            World world = Client.Game?.UO?.World;
            if (world == null)
                return -1;

            world.NameOverHeadManager.ClearAllOverrides();
            return 0;
        }

        private static int WriteSupportedOpcodes(IntPtr outBuf, int outCap)
        {
            ReadOnlySpan<int> opcodes =
            [
                GenericOpcodes.GetSupportedOpcodes,
                GenericOpcodes.ListOpenStatusBars,
                GenericOpcodes.Attack,
                GenericOpcodes.SetNameplateOverride,
                GenericOpcodes.ClearNameplateOverride,
                GenericOpcodes.ClearAllNameplateOverrides,
            ];

            int needed = opcodes.Length * sizeof(int);
            if (outCap < needed)
                return -2; // output buffer too small

            var dst = new Span<int>((void*)outBuf, opcodes.Length);
            opcodes.CopyTo(dst);
            return needed;
        }
    }
}
