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

    [Fact]
    public void Execute_ShouldHandleEqRegisterComparison()
    {
        var main = new LuaPrototype
        {
            LineDefined = 0,
            LastLineDefined = 0,
            NumberOfParameters = 0,
            Flags = 0,
            MaxStackSize = 3,
            Code =
            [
                EncodeAsBx(LuaOpcode.LoadI, a: 0, sBx: 7),
                EncodeAsBx(LuaOpcode.LoadI, a: 1, sBx: 7),
                EncodeAbc(LuaOpcode.Eq, a: 0, b: 1, c: 0, k: 0),
                EncodeSJ(LuaOpcode.Jmp, sJ: 2),
                EncodeAsBx(LuaOpcode.LoadI, a: 2, sBx: 1),
                EncodeAbc(LuaOpcode.Return1, a: 2, b: 0, c: 0),
                EncodeAsBx(LuaOpcode.LoadI, a: 2, sBx: 0),
                EncodeAbc(LuaOpcode.Return1, a: 2, b: 0, c: 0)
            ],
            Constants = [],
            Upvalues = [],
            NestedPrototypes = [],
            Source = "eq_test.lua",
            LineInfo = [0, 0, 0, 0, 0, 0, 0, 0],
            AbsoluteLineInfo = [],
            LocalVariables = []
        };

        var vm = new LuaVirtualMachine();
        var results = vm.Execute(CreateChunk(main));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(1);
    }

    [Fact]
    public void Execute_ShouldHandleLeRegisterComparison()
    {
        var main = new LuaPrototype
        {
            LineDefined = 0,
            LastLineDefined = 0,
            NumberOfParameters = 0,
            Flags = 0,
            MaxStackSize = 3,
            Code =
            [
                EncodeAsBx(LuaOpcode.LoadI, a: 0, sBx: 4),
                EncodeAsBx(LuaOpcode.LoadI, a: 1, sBx: 4),
                EncodeAbc(LuaOpcode.Le, a: 0, b: 1, c: 0, k: 0),
                EncodeSJ(LuaOpcode.Jmp, sJ: 2),
                EncodeAsBx(LuaOpcode.LoadI, a: 2, sBx: 1),
                EncodeAbc(LuaOpcode.Return1, a: 2, b: 0, c: 0),
                EncodeAsBx(LuaOpcode.LoadI, a: 2, sBx: 0),
                EncodeAbc(LuaOpcode.Return1, a: 2, b: 0, c: 0)
            ],
            Constants = [],
            Upvalues = [],
            NestedPrototypes = [],
            Source = "le_test.lua",
            LineInfo = [0, 0, 0, 0, 0, 0, 0, 0],
            AbsoluteLineInfo = [],
            LocalVariables = []
        };

        var vm = new LuaVirtualMachine();
        var results = vm.Execute(CreateChunk(main));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(1);
    }

    [Fact]
    public void Execute_ShouldHandleEqkChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "eqk_chunk.luac")), "eqk_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(1);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleLtChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "lt_chunk.luac")), "lt_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("lt");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleTestSetChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "testset_chunk.luac")), "testset_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("fallback");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleArithmeticChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "arith_chunk.luac")), "arith_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(8);
        results[0].AsInteger().ShouldBe(42);
        results[1].AsInteger().ShouldBe(34);
        results[2].AsInteger().ShouldBe(240);
        results[3].AsFloat().ShouldBe(40d / 6d, 1e-12);
        results[4].AsInteger().ShouldBe(6);
        results[5].AsInteger().ShouldBe(4);
        results[6].AsInteger().ShouldBe(-6);
        results[7].AsBoolean().ShouldBeTrue();
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleAddkChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "addk_chunk.luac")), "addk_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsFloat().ShouldBe(42.5d, 1e-12);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleNotChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "not_chunk.luac")), "not_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsBoolean().ShouldBeTrue();
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleFloorDivisionChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "floor_div_chunk.luac")), "floor_div_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(-3);
        results[1].AsInteger().ShouldBe(2);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleKOpsChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "k_ops_chunk.luac")), "k_ops_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(8);
        results[0].AsFloat().ShouldBe(17.5d, 1e-12);
        results[1].AsInteger().ShouldBe(80);
        results[2].AsFloat().ShouldBe(10d, 1e-12);
        results[3].AsInteger().ShouldBe(3);
        results[4].AsInteger().ShouldBe(2);
        results[5].AsInteger().ShouldBe(4);
        results[6].AsInteger().ShouldBe(21);
        results[7].AsInteger().ShouldBe(23);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleBitChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "bit_chunk.luac")), "bit_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(8);
        results[0].AsInteger().ShouldBe(2);
        results[1].AsInteger().ShouldBe(7);
        results[2].AsInteger().ShouldBe(5);
        results[3].AsInteger().ShouldBe(48);
        results[4].AsInteger().ShouldBe(0);
        results[5].AsInteger().ShouldBe(3);
        results[6].AsInteger().ShouldBe(16);
        results[7].AsInteger().ShouldBe(~6L);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandlePowChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "pow_chunk.luac")), "pow_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsFloat().ShouldBe(32d, 1e-12);
        results[1].AsFloat().ShouldBe(4d, 1e-12);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleStringChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "str_chunk.luac")), "str_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(3);
        results[1].AsString().ShouldBe("lua-net");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleTableChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "table_chunk.luac")), "table_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(42);
        results[1].AsString().ShouldBe("lua");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleDynamicTableChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "table_dynamic_chunk.luac")), "table_dynamic_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(42);
        results[1].AsString().ShouldBe("lua");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleSelfChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "self_chunk.luac")), "self_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("lua-net");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleGlobalChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "global_chunk.luac")), "global_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.GlobalEnvironment.GetValue(LuaValue.FromString("answer")).AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleUpvalueChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "upvalue_chunk.luac")), "upvalue_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(41);
        results[1].AsInteger().ShouldBe(41);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleCloseChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "close_chunk.luac")), "close_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(40);
        results[1].AsInteger().ShouldBe(99);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleTbcNilChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "tbc_nil_chunk.luac")), "tbc_nil_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleTbcFalseChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "tbc_false_chunk.luac")), "tbc_false_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
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
