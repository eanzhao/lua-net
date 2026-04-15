using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Shouldly;
using System.Text;

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
    public void Execute_ShouldHandleSetListChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "setlist_chunk.luac")), "setlist_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(3);
        results[0].AsInteger().ShouldBe(10);
        results[1].AsInteger().ShouldBe(20);
        results[2].AsInteger().ShouldBe(30);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleSetListExtraArgChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "setlist_extraarg_chunk.luac")), "setlist_extraarg_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(4);
        results[0].AsInteger().ShouldBe(1);
        results[1].AsInteger().ShouldBe(1024);
        results[2].AsInteger().ShouldBe(1050);
        results[3].AsInteger().ShouldBe(1100);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleVarArgFixedChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "vararg_fixed_chunk.luac")), "vararg_fixed_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(3);
        results[0].AsInteger().ShouldBe(10);
        results[1].AsInteger().ShouldBe(20);
        results[2].AsInteger().ShouldBe(30);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleVarArgAllChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "vararg_all_chunk.luac")), "vararg_all_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(3);
        results[0].AsString().ShouldBe("x");
        results[1].AsString().ShouldBe("y");
        results[2].AsString().ShouldBe("z");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleOpenCallChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "open_call_chunk.luac")), "open_call_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(3);
        results[0].AsInteger().ShouldBe(1);
        results[1].AsInteger().ShouldBe(2);
        results[2].AsInteger().ShouldBe(3);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleOpenSetListChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "setlist_open_chunk.luac")), "setlist_open_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(3);
        results[0].AsInteger().ShouldBe(4);
        results[1].AsInteger().ShouldBe(5);
        results[2].AsInteger().ShouldBe(6);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleGetVarArgInstruction()
    {
        var callee = new LuaPrototype
        {
            LineDefined = 1,
            LastLineDefined = 1,
            NumberOfParameters = 0,
            Flags = 1,
            MaxStackSize = 4,
            Code =
            [
                EncodeAbc(LuaOpcode.VarArgPrep, a: 0, b: 0, c: 0),
                EncodeAsBx(LuaOpcode.LoadI, a: 0, sBx: 2),
                EncodeAbc(LuaOpcode.GetVArg, a: 1, b: 0, c: 0),
                EncodeAbx(LuaOpcode.LoadK, a: 2, bx: 0),
                EncodeAbc(LuaOpcode.GetVArg, a: 2, b: 0, c: 2),
                EncodeAbc(LuaOpcode.Return, a: 1, b: 3, c: 0)
            ],
            Constants =
            [
                LuaConstant.FromString("n")
            ],
            Upvalues = [],
            NestedPrototypes = [],
            Source = "getvarg_test.lua",
            LineInfo = [0, 0, 0, 0, 0, 0],
            AbsoluteLineInfo = [],
            LocalVariables = []
        };

        var main = new LuaPrototype
        {
            LineDefined = 0,
            LastLineDefined = 0,
            NumberOfParameters = 0,
            Flags = 0,
            MaxStackSize = 4,
            Code =
            [
                EncodeAbx(LuaOpcode.Closure, a: 0, bx: 0),
                EncodeAsBx(LuaOpcode.LoadI, a: 1, sBx: 10),
                EncodeAsBx(LuaOpcode.LoadI, a: 2, sBx: 20),
                EncodeAsBx(LuaOpcode.LoadI, a: 3, sBx: 30),
                EncodeAbc(LuaOpcode.Call, a: 0, b: 4, c: 3),
                EncodeAbc(LuaOpcode.Return, a: 0, b: 3, c: 0)
            ],
            Constants = [],
            Upvalues = [],
            NestedPrototypes = [callee],
            Source = "getvarg_test.lua",
            LineInfo = [0, 0, 0, 0, 0, 0],
            AbsoluteLineInfo = [],
            LocalVariables = []
        };

        var vm = new LuaVirtualMachine();
        var results = vm.Execute(CreateChunk(main));

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(20);
        results[1].AsInteger().ShouldBe(3);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleIntegerForChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "for_integer_chunk.luac")), "for_integer_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(9);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleFloatForChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "for_float_chunk.luac")), "for_float_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsFloat().ShouldBe(9d, 1e-12);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldRaiseLuaRuntimeExceptionForZeroIntegerForStep()
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
                EncodeAsBx(LuaOpcode.LoadI, a: 0, sBx: 1),
                EncodeAsBx(LuaOpcode.LoadI, a: 1, sBx: 10),
                EncodeAsBx(LuaOpcode.LoadI, a: 2, sBx: 0),
                EncodeAbx(LuaOpcode.ForPrep, a: 0, bx: 0),
                EncodeAbc(LuaOpcode.Return, a: 0, b: 1, c: 0)
            ],
            Constants = [],
            Upvalues = [],
            NestedPrototypes = [],
            Source = "for_zero_step_test.lua",
            LineInfo = [0, 0, 0, 0, 0],
            AbsoluteLineInfo = [],
            LocalVariables = []
        };

        var vm = new LuaVirtualMachine();

        var exception = Should.Throw<LuaRuntimeException>(() => vm.Execute(CreateChunk(main)));

        exception.ErrorObject.AsString().ShouldBe("'for' step is zero");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleGenericForChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "for_generic_chunk.luac")), "for_generic_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(66);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleWhileChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "while_chunk.luac")), "while_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(6);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleRepeatChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "repeat_chunk.luac")), "repeat_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(6);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleGlobalOkChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "global_ok_chunk.luac")), "global_ok_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.GlobalEnvironment.GetValue(LuaValue.FromString("answer")).AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldRaiseGlobalAlreadyDefinedErrorForErrNNil()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "global_err_chunk.luac")), "global_err_chunk.luac");
        var vm = new LuaVirtualMachine();

        var exception = Should.Throw<LuaRuntimeException>(() => vm.Execute(chunk));

        exception.ErrorObject.AsString().ShouldBe("global 'answer' already defined");
        vm.State.GlobalEnvironment.GetValue(LuaValue.FromString("answer")).AsInteger().ShouldBe(1);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleVarArgTableReturnChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "vararg_table_return_chunk.luac")), "vararg_table_return_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].Kind.ShouldBe(LuaValueKind.Table);
        var table = results[0].AsTable();
        table.GetValue(LuaValue.FromInteger(1)).AsInteger().ShouldBe(10);
        table.GetValue(LuaValue.FromInteger(2)).AsInteger().ShouldBe(20);
        table.GetValue(LuaValue.FromInteger(3)).AsInteger().ShouldBe(30);
        table.GetValue(LuaValue.FromString("n")).AsInteger().ShouldBe(3);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleVarArgTableMixChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "vararg_table_mix_chunk.luac")), "vararg_table_mix_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(10);
        results[1].Kind.ShouldBe(LuaValueKind.Table);
        var table = results[1].AsTable();
        table.GetValue(LuaValue.FromInteger(2)).AsInteger().ShouldBe(20);
        table.GetValue(LuaValue.FromString("n")).AsInteger().ShouldBe(3);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleVarArgTableMutationChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "vararg_table_mutation_chunk.luac")), "vararg_table_mutation_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(99);
        results[1].AsInteger().ShouldBe(3);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaAddChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_add_chunk.luac")), "meta_add_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaAddImmediateChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_addi_chunk.luac")), "meta_addi_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaFlipChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_flip_chunk.luac")), "meta_flip_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(48);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaAddConstantChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_addk_chunk.luac")), "meta_addk_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsFloat().ShouldBe(42d, 1e-12);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaLengthChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_len_chunk.luac")), "meta_len_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaConcatChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_concat_chunk.luac")), "meta_concat_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("lua-net");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaEqualityChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_eq_chunk.luac")), "meta_eq_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(1);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaLessThanChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_lt_chunk.luac")), "meta_lt_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("lt");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaLessEqualChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_le_chunk.luac")), "meta_le_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("le");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaLessThanImmediateChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_lti_chunk.luac")), "meta_lti_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("lti");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaGreaterThanImmediateChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_gti_chunk.luac")), "meta_gti_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("gti");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaLessEqualImmediateChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_lei_chunk.luac")), "meta_lei_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("lei");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaGreaterEqualImmediateChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_gei_chunk.luac")), "meta_gei_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("gei");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaUnaryMinusChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_unm_chunk.luac")), "meta_unm_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaBitwiseNotChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_bnot_chunk.luac")), "meta_bnot_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(5);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaCallChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_call_chunk.luac")), "meta_call_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(43);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaTailCallChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_tailcall_chunk.luac")), "meta_tailcall_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(43);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaIndexTableChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_index_table_chunk.luac")), "meta_index_table_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaIndexFunctionChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_index_function_chunk.luac")), "meta_index_function_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("lua-net");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaNewIndexTableChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_newindex_table_chunk.luac")), "meta_newindex_table_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(42);
        results[1].IsNil.ShouldBeTrue();
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleMetaNewIndexFunctionChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_newindex_function_chunk.luac")), "meta_newindex_function_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldBypassMetaNewIndexForExistingKeyChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "meta_newindex_existing_chunk.luac")), "meta_newindex_existing_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(42);
        results[1].IsNil.ShouldBeTrue();
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

    [Fact]
    public void Execute_ShouldHandleTbcCloseChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "tbc_close_chunk.luac")), "tbc_close_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(42);
        results[1].AsString().ShouldBe("xba");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleLoadfChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "loadf_chunk.luac")), "loadf_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsFloat().ShouldBe(3d, 1e-12);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleLFalseSkipChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "lfalseskip_chunk.luac")), "lfalseskip_chunk.luac");
        var vm = new LuaVirtualMachine();

        var results = vm.Execute(chunk);

        results.ShouldHaveSingleItem();
        results[0].AsBoolean().ShouldBeTrue();
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleLoadKxInstruction()
    {
        var main = new LuaPrototype
        {
            LineDefined = 0,
            LastLineDefined = 0,
            NumberOfParameters = 0,
            Flags = 0,
            MaxStackSize = 1,
            Code =
            [
                EncodeAbx(LuaOpcode.LoadKx, a: 0, bx: 0),
                EncodeAx(LuaOpcode.ExtraArg, ax: 1),
                EncodeAbc(LuaOpcode.Return1, a: 0, b: 0, c: 0)
            ],
            Constants =
            [
                LuaConstant.FromString("first"),
                LuaConstant.FromString("second")
            ],
            Upvalues = [],
            NestedPrototypes = [],
            Source = "loadkx_test.lua",
            LineInfo = [0, 0, 0],
            AbsoluteLineInfo = [],
            LocalVariables = []
        };

        var vm = new LuaVirtualMachine();
        var results = vm.Execute(CreateChunk(main));

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("second");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldPropagateLatestCloseErrorAndContinueClosing()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "tbc_error_chunk.luac")), "tbc_error_chunk.luac");
        var vm = new LuaVirtualMachine();

        var exception = Should.Throw<LuaRuntimeException>(() => vm.Execute(chunk));

        exception.ErrorObject.AsString().ShouldBe("close-b");
        vm.State.GlobalEnvironment.GetValue(LuaValue.FromString("log")).AsString().ShouldBe("[b:boom][a:close-b]");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleUserDataIndexWithTableMetamethod()
    {
        var vm = new LuaVirtualMachine();
        var userData = new LuaUserData(new object());
        var fallback = new LuaTable();
        var metatable = new LuaTable();

        fallback.SetValue(LuaValue.FromString("answer"), LuaValue.FromInteger(42));
        metatable.SetValue(LuaValue.FromString("__index"), LuaValue.FromTable(fallback));
        userData.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(userData));

        var results = vm.Execute(ReadFixtureChunk("userdata_index_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleUserDataIndexWithFunctionMetamethod()
    {
        var vm = new LuaVirtualMachine();
        var userData = new LuaUserData(new object());
        var metatable = new LuaTable();

        metatable.SetValue(
            LuaValue.FromString("__index"),
            LuaValue.FromFunction(CreateNativeClosure(
                "__index",
                static (_, _, arguments) => [LuaValue.FromString(arguments[1].AsString() + "-value")])));
        userData.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(userData));

        var results = vm.Execute(ReadFixtureChunk("userdata_index_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("answer-value");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleUserDataNewIndexWithTableMetamethod()
    {
        var vm = new LuaVirtualMachine();
        var userData = new LuaUserData(new object());
        var sink = new LuaTable();
        var metatable = new LuaTable();

        metatable.SetValue(LuaValue.FromString("__newindex"), LuaValue.FromTable(sink));
        userData.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(userData));
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("sink"), LuaValue.FromTable(sink));

        var results = vm.Execute(ReadFixtureChunk("userdata_newindex_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleUserDataNewIndexWithFunctionMetamethod()
    {
        var vm = new LuaVirtualMachine();
        var userData = new LuaUserData(new object());
        var sink = new LuaTable();
        var metatable = new LuaTable();

        metatable.SetValue(
            LuaValue.FromString("__newindex"),
            LuaValue.FromFunction(CreateNativeClosure(
                "__newindex",
                (_, _, arguments) =>
                {
                    sink.SetValue(arguments[1], arguments[2]);
                    return [];
                })));
        userData.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(userData));
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("sink"), LuaValue.FromTable(sink));

        var results = vm.Execute(ReadFixtureChunk("userdata_newindex_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleUserDataCallMetamethod()
    {
        var vm = new LuaVirtualMachine();
        var userData = new LuaUserData(new object());
        var metatable = new LuaTable();

        metatable.SetValue(
            LuaValue.FromString("__call"),
            LuaValue.FromFunction(CreateNativeClosure(
                "__call",
                static (_, _, arguments) => [LuaValue.FromInteger(arguments[1].AsInteger() + 1)])));
        userData.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(userData));

        var results = vm.Execute(ReadFixtureChunk("userdata_call_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleUserDataLengthMetamethod()
    {
        var vm = new LuaVirtualMachine();
        var userData = new LuaUserData(new object());
        var metatable = new LuaTable();

        metatable.SetValue(
            LuaValue.FromString("__len"),
            LuaValue.FromFunction(CreateNativeClosure(
                "__len",
                static (_, _, _) => [LuaValue.FromInteger(42)])));
        userData.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(userData));

        var results = vm.Execute(ReadFixtureChunk("userdata_len_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleUserDataUnaryMinusMetamethod()
    {
        var vm = new LuaVirtualMachine();
        var userData = new LuaUserData(new object());
        var metatable = new LuaTable();

        metatable.SetValue(
            LuaValue.FromString("__unm"),
            LuaValue.FromFunction(CreateNativeClosure(
                "__unm",
                static (_, _, _) => [LuaValue.FromInteger(42)])));
        userData.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(userData));

        var results = vm.Execute(ReadFixtureChunk("userdata_unm_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleUserDataEqualityMetamethod()
    {
        var vm = new LuaVirtualMachine();
        var left = new LuaUserData("left");
        var right = new LuaUserData("right");
        var metatable = new LuaTable();

        metatable.SetValue(
            LuaValue.FromString("__eq"),
            LuaValue.FromFunction(CreateNativeClosure(
                "__eq",
                static (_, _, _) => [LuaValue.FromBoolean(true)])));
        left.SetMetatable(metatable);
        right.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(left));
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud2"), LuaValue.FromUserData(right));

        var results = vm.Execute(ReadFixtureChunk("userdata_eq_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(1);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleUserDataCloseMetamethod()
    {
        var vm = new LuaVirtualMachine();
        var userData = new LuaUserData(new object());
        var metatable = new LuaTable();
        var closed = false;

        metatable.SetValue(
            LuaValue.FromString("__close"),
            LuaValue.FromFunction(CreateNativeClosure(
                "__close",
                (_, _, arguments) =>
                {
                    arguments[0].Kind.ShouldBe(LuaValueKind.UserData);
                    closed = true;
                    return [];
                })));
        userData.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(userData));

        var results = vm.Execute(ReadFixtureChunk("userdata_close_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
        closed.ShouldBeTrue();
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleRawMetatableChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var results = vm.Execute(ReadFixtureChunk("raw_metatable_chunk.luac"));

        results.Length.ShouldBe(8);
        results[0].AsInteger().ShouldBe(41);
        results[1].AsInteger().ShouldBe(42);
        results[2].IsNil.ShouldBeTrue();
        results[3].AsInteger().ShouldBe(2);
        results[4].AsInteger().ShouldBe(3);
        results[5].AsInteger().ShouldBe(1);
        results[6].AsInteger().ShouldBe(1);
        results[7].AsInteger().ShouldBe(1);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleProtectedMetatableChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var results = vm.Execute(ReadFixtureChunk("protected_metatable_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("locked");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleUserDataGetMetatableChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var userData = new LuaUserData(new object());
        var metatable = new LuaTable();

        userData.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(userData));
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("mt"), LuaValue.FromTable(metatable));

        var results = vm.Execute(ReadFixtureChunk("userdata_getmetatable_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(1);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleTypeChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(new LuaUserData(new object())));

        var results = vm.Execute(ReadFixtureChunk("type_chunk.luac"));

        results.Length.ShouldBe(6);
        results[0].AsString().ShouldBe("nil");
        results[1].AsString().ShouldBe("number");
        results[2].AsString().ShouldBe("string");
        results[3].AsString().ShouldBe("table");
        results[4].AsString().ShouldBe("function");
        results[5].AsString().ShouldBe("userdata");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleAssertAndSelectChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var results = vm.Execute(ReadFixtureChunk("select_chunk.luac"));

        results.Length.ShouldBe(7);
        results[0].AsInteger().ShouldBe(3);
        results[1].AsString().ShouldBe("ok");
        results[2].AsInteger().ShouldBe(10);
        results[3].AsInteger().ShouldBe(20);
        results[4].AsInteger().ShouldBe(10);
        results[5].AsInteger().ShouldBe(20);
        results[6].AsInteger().ShouldBe(20);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandlePCallChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var userData = new LuaUserData(new object());
        var metatable = new LuaTable();

        metatable.SetValue(
            LuaValue.FromString("__call"),
            LuaValue.FromFunction(CreateNativeClosure(
                "__call",
                static (_, _, arguments) => [LuaValue.FromInteger(arguments[1].AsInteger() + 1)])));
        userData.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(userData));

        var results = vm.Execute(ReadFixtureChunk("pcall_chunk.luac"));

        results.Length.ShouldBe(6);
        results[0].AsInteger().ShouldBe(42);
        results[1].AsInteger().ShouldBe(42);
        results[2].AsInteger().ShouldBe(0);
        results[3].AsString().ShouldBe("boom");
        results[4].AsInteger().ShouldBe(0);
        results[5].AsString().ShouldBe("assert-fail");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleXPCallChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var userData = new LuaUserData(new object());
        var metatable = new LuaTable();

        metatable.SetValue(
            LuaValue.FromString("__call"),
            LuaValue.FromFunction(CreateNativeClosure(
                "__call",
                static (_, _, arguments) => [LuaValue.FromInteger(arguments[1].AsInteger() + 1)])));
        userData.SetMetatable(metatable);
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("ud"), LuaValue.FromUserData(userData));

        var results = vm.Execute(ReadFixtureChunk("xpcall_chunk.luac"));

        results.Length.ShouldBe(6);
        results[0].AsInteger().ShouldBe(42);
        results[1].AsInteger().ShouldBe(42);
        results[2].AsInteger().ShouldBe(0);
        results[3].AsString().ShouldBe("handled:boom");
        results[4].AsInteger().ShouldBe(0);
        results[5].AsString().ShouldBe("error in error handling");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleToNumberChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var results = vm.Execute(ReadFixtureChunk("tonumber_chunk.luac"));

        results.Length.ShouldBe(7);
        results[0].AsInteger().ShouldBe(16);
        results[1].AsFloat().ShouldBe(3.0d);
        results[2].AsFloat().ShouldBe(3.5d);
        results[3].AsInteger().ShouldBe(255);
        results[4].AsInteger().ShouldBe(-2);
        results[5].AsString().ShouldBe("nil");
        results[6].AsString().ShouldBe("nil");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleToStringChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var results = vm.Execute(ReadFixtureChunk("tostring_chunk.luac"));

        results.Length.ShouldBe(4);
        results[0].AsString().ShouldBe("3.0");
        results[1].AsString().ShouldStartWith("vec: 0x");
        results[2].AsString().ShouldBe("custom");
        results[3].AsString().ShouldStartWith("function: 0x");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleIteratorChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var results = vm.Execute(ReadFixtureChunk("iterators_chunk.luac"));

        results.Length.ShouldBe(9);
        results[0].AsString().ShouldBe("only");
        results[1].AsInteger().ShouldBe(42);
        results[2].AsInteger().ShouldBe(1);
        results[3].AsInteger().ShouldBe(3);
        results[4].AsInteger().ShouldBe(60);
        results[5].AsString().ShouldBe("tag");
        results[6].AsInteger().ShouldBe(99);
        results[7].AsInteger().ShouldBe(2);
        results[8].AsInteger().ShouldBe(33);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandlePrintWarnChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var lines = new List<string>();
        var warnings = new List<string>();

        vm.State.PrintOutput = lines.Add;
        vm.State.WarningOutput = warnings.Add;

        var results = vm.Execute(ReadFixtureChunk("print_warn_chunk.luac"));

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(99);
        lines.Count.ShouldBe(2);
        lines[0].ShouldBe("head\t42");
        lines[1].ShouldBe("obj\t3.0");
        warnings.ShouldHaveSingleItem();
        warnings[0].ShouldBe("Lua warning: abc");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleStringMethodChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var results = vm.Execute(ReadFixtureChunk("string_method_chunk.luac"));

        results.Length.ShouldBe(4);
        results[0].AsString().ShouldBe("LUA");
        results[1].AsString().ShouldBe("net");
        results[2].AsInteger().ShouldBe(3);
        results[3].AsString().ShouldBe("ABC");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleStringArithmeticChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var results = vm.Execute(ReadFixtureChunk("string_arith_chunk.luac"));

        results.Length.ShouldBe(9);
        results[0].AsInteger().ShouldBe(11);
        results[1].AsInteger().ShouldBe(9);
        results[2].AsInteger().ShouldBe(42);
        results[3].AsInteger().ShouldBe(1);
        results[4].AsFloat().ShouldBe(8.0d);
        results[5].AsFloat().ShouldBe(3.5d);
        results[6].AsInteger().ShouldBe(3);
        results[7].AsInteger().ShouldBe(-5);
        results[8].AsString().ShouldBe("fallback");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleLoadChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var loadTargetPath = GetFixturePath("chunks", "load_env_chunk.luac");
        var loadTargetText = Encoding.Latin1.GetString(File.ReadAllBytes(loadTargetPath));
        var readerPieces = new Queue<LuaValue>(
        [
            LuaValue.FromString(loadTargetText[..5]),
            LuaValue.FromString(loadTargetText[5..]),
            LuaValue.Nil
        ]);
        var reader = new LuaClosure(
            "reader",
            body: new LuaNativeClosureBody((_, _, _) =>
            {
                var next = readerPieces.Dequeue();
                return [next];
            }));

        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("chunk"), LuaValue.FromString(loadTargetText));
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("path"), LuaValue.FromString(loadTargetPath));
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("reader"), LuaValue.FromFunction(reader));

        var results = vm.Execute(ReadFixtureChunk("load_chunk.luac"));

        results.Length.ShouldBe(4);
        results[0].AsInteger().ShouldBe(41);
        results[1].AsInteger().ShouldBe(42);
        results[2].AsInteger().ShouldBe(7);
        results[3].AsInteger().ShouldBe(11);
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleCollectGarbageChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var results = vm.Execute(ReadFixtureChunk("collectgarbage_chunk.luac"));

        results.Length.ShouldBe(8);
        results[0].AsBoolean().ShouldBeTrue();
        results[1].AsInteger().ShouldBe(0);
        results[2].AsBoolean().ShouldBeFalse();
        results[3].AsBoolean().ShouldBeTrue();
        results[4].AsBoolean().ShouldBeFalse();
        results[5].AsInteger().ShouldBe(0);
        results[6].AsInteger().ShouldBe(0);
        results[7].AsBoolean().ShouldBeTrue();
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Execute_ShouldHandleRequireChunkFixture()
    {
        var vm = new LuaVirtualMachine();
        var modulePath = GetFixturePath("chunks", "?.luac");

        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("module_path"), LuaValue.FromString(modulePath));

        var results = vm.Execute(ReadFixtureChunk("require_chunk.luac"));

        results.Length.ShouldBe(13);
        results[0].AsInteger().ShouldBe(1);
        results[1].AsBoolean().ShouldBeTrue();
        results[2].AsString().ShouldBe("pre_mod");
        results[3].AsString().ShouldBe(":preload:");
        results[4].AsString().ShouldBe(":preload:");
        results[5].AsInteger().ShouldBe(1);
        results[6].AsInteger().ShouldBe(77);
        results[7].AsInteger().ShouldBe(77);
        results[8].AsString().ShouldBe(GetFixturePath("chunks", "require_file_chunk.luac"));
        results[9].AsInteger().ShouldBe(1);
        results[10].AsBoolean().ShouldBeTrue();
        results[11].AsBoolean().ShouldBeTrue();
        results[12].AsString().ShouldBe(":preload:");
        vm.State.Frames.ShouldBeEmpty();
        vm.State.Stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Call_ShouldWrapClrExceptionsFromNativeClosures()
    {
        var closure = new LuaClosure(
            "boom",
            body: new LuaNativeClosureBody(static (_, _, _) => throw new InvalidOperationException("boom")));
        var vm = new LuaVirtualMachine();

        var exception = Should.Throw<LuaRuntimeException>(() => vm.Call(closure));

        exception.ErrorObject.AsString().ShouldBe("boom");
        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    private static string GetFixturePath(string folder, string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", "lua55", folder, fileName);
    }

    private static LuaChunk ReadFixtureChunk(string fileName)
    {
        var reader = new LuaChunkReader();
        return reader.Read(File.ReadAllBytes(GetFixturePath("chunks", fileName)), fileName);
    }

    private static LuaClosure CreateNativeClosure(string debugName, LuaNativeFunction function)
    {
        return new LuaClosure(debugName, body: new LuaNativeClosureBody(function));
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

    private static uint EncodeAx(LuaOpcode opcode, int ax)
    {
        return
            ((uint)opcode << LuaInstructionLayout.PosOp) |
            ((uint)ax << LuaInstructionLayout.PosAx);
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
