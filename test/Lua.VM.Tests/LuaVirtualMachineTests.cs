using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Runtime.Values;
using Shouldly;

namespace Lua.VM.Tests;

public class LuaVirtualMachineTests
{
    [Fact]
    public void Execute_ShouldRunNestedChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "nested_chunk.luac")), "nested_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(42);
        results[1].AsString().ShouldBe("lua");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleFixedResultCall()
    {
        var callee = new LuaPrototype
        {
            LineDefined = 1,
            LastLineDefined = 1,
            NumberOfParameters = 2,
            Flags = 0,
            MaxStackSize = 3,
            Code =
            [
                EncodeAbc(LuaOpcode.Add, a: 2, b: 0, c: 1),
                EncodeAbc(LuaOpcode.Return1, a: 2, b: 0, c: 0)
            ],
            Constants = [],
            Upvalues = [],
            NestedPrototypes = [],
            Source = "call_test.lua",
            LineInfo = [0, 0],
            AbsoluteLineInfo = [],
            LocalVariables = []
        };

        var main = new LuaPrototype
        {
            LineDefined = 0,
            LastLineDefined = 0,
            NumberOfParameters = 0,
            Flags = 0,
            MaxStackSize = 3,
            Code =
            [
                EncodeAbx(LuaOpcode.Closure, a: 0, bx: 0),
                EncodeAsBx(LuaOpcode.LoadI, a: 1, sBx: 20),
                EncodeAsBx(LuaOpcode.LoadI, a: 2, sBx: 22),
                EncodeAbc(LuaOpcode.Call, a: 0, b: 3, c: 2),
                EncodeAbc(LuaOpcode.Return1, a: 0, b: 0, c: 0)
            ],
            Constants = [],
            Upvalues = [],
            NestedPrototypes = [callee],
            Source = "call_test.lua",
            LineInfo = [0, 0, 0, 0, 0],
            AbsoluteLineInfo = [],
            LocalVariables = []
        };

        var vm = new LuaVirtualMachine();
        var results = vm.Execute(CreateChunk(main));

        results.ShouldHaveSingleItem();
        results[0].ShouldBe(LuaValue.FromInteger(42));
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleBooleanAndNilLoads()
    {
        var main = new LuaPrototype
        {
            LineDefined = 0,
            LastLineDefined = 0,
            NumberOfParameters = 0,
            Flags = 0,
            MaxStackSize = 4,
            Code =
            [
                EncodeAbc(LuaOpcode.LoadTrue, a: 0, b: 0, c: 0),
                EncodeAbc(LuaOpcode.LoadFalse, a: 1, b: 0, c: 0),
                EncodeAbc(LuaOpcode.LoadNil, a: 2, b: 1, c: 0),
                EncodeAbc(LuaOpcode.Return, a: 0, b: 4, c: 0)
            ],
            Constants = [],
            Upvalues = [],
            NestedPrototypes = [],
            Source = "load_test.lua",
            LineInfo = [0, 0, 0, 0],
            AbsoluteLineInfo = [],
            LocalVariables = []
        };

        var vm = new LuaVirtualMachine();
        var results = vm.Execute(CreateChunk(main));

        results.Length.ShouldBe(3);
        results[0].AsBoolean().ShouldBeTrue();
        results[1].AsBoolean().ShouldBeFalse();
        results[2].IsNil.ShouldBeTrue();
    }

    [Fact]
    public void Execute_ShouldHandleTestInstruction()
    {
        var main = new LuaPrototype
        {
            LineDefined = 0,
            LastLineDefined = 0,
            NumberOfParameters = 0,
            Flags = 0,
            MaxStackSize = 2,
            Code =
            [
                EncodeAbc(LuaOpcode.LoadTrue, a: 0, b: 0, c: 0),
                EncodeAbc(LuaOpcode.Test, a: 0, b: 0, c: 0, k: 1),
                EncodeSJ(LuaOpcode.Jmp, sJ: 2),
                EncodeAbx(LuaOpcode.LoadK, a: 1, bx: 0),
                EncodeAbc(LuaOpcode.Return1, a: 1, b: 0, c: 0),
                EncodeAbx(LuaOpcode.LoadK, a: 1, bx: 1),
                EncodeAbc(LuaOpcode.Return1, a: 1, b: 0, c: 0)
            ],
            Constants =
            [
                LuaConstant.FromString("wrong"),
                LuaConstant.FromString("ok")
            ],
            Upvalues = [],
            NestedPrototypes = [],
            Source = "test_jump.lua",
            LineInfo = [0, 0, 0, 0, 0, 0, 0],
            AbsoluteLineInfo = [],
            LocalVariables = []
        };

        var vm = new LuaVirtualMachine();
        var results = vm.Execute(CreateChunk(main));

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("ok");
    }

    [Fact]
    public void Execute_ShouldHandleBranchChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "branch_chunk.luac")), "branch_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("big");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    private static string GetFixturePath(string folder, string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", "lua55", folder, fileName);
    }

    private static uint EncodeAbc(LuaOpcode opcode, int a, int b, int c, int k = 0)
    {
        return
            ((uint)opcode << LuaInstructionLayout.PosOp) |
            ((uint)a << LuaInstructionLayout.PosA) |
            ((uint)k << LuaInstructionLayout.PosK) |
            ((uint)b << LuaInstructionLayout.PosB) |
            ((uint)c << LuaInstructionLayout.PosC);
    }

    private static uint EncodeAbx(LuaOpcode opcode, int a, int bx)
    {
        return
            ((uint)opcode << LuaInstructionLayout.PosOp) |
            ((uint)a << LuaInstructionLayout.PosA) |
            ((uint)bx << LuaInstructionLayout.PosBx);
    }

    private static uint EncodeAsBx(LuaOpcode opcode, int a, int sBx)
    {
        var encoded = (uint)(sBx + LuaInstructionLayout.OffsetSBx);
        return EncodeAbx(opcode, a, (int)encoded);
    }

    private static uint EncodeSJ(LuaOpcode opcode, int sJ)
    {
        var encoded = (uint)(sJ + LuaInstructionLayout.OffsetSJ);
        return
            ((uint)opcode << LuaInstructionLayout.PosOp) |
            (encoded << LuaInstructionLayout.PosSJ);
    }

    private static LuaChunk CreateChunk(LuaPrototype mainFunction)
    {
        return new LuaChunk
        {
            Header = new LuaChunkHeader
            {
                Version = LuaChunkHeaderConstants.LuacVersion,
                Format = LuaChunkHeaderConstants.LuacFormat,
                IntSize = (byte)sizeof(int),
                IntFormatMarker = LuaChunkHeaderConstants.LuacInt,
                InstructionSize = (byte)sizeof(uint),
                InstructionFormatMarker = LuaChunkHeaderConstants.LuacInstruction,
                LuaIntegerSize = (byte)sizeof(long),
                LuaIntegerFormatMarker = LuaChunkHeaderConstants.LuacInt,
                LuaNumberSize = (byte)sizeof(double),
                LuaNumberFormatMarker = LuaChunkHeaderConstants.LuacNumber
            },
            MainUpvalueCount = 0,
            MainFunction = mainFunction
        };
    }
}
