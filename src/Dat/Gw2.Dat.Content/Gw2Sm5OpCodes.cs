namespace Gw2.Dat.Content;

public static class Gw2Sm5OpCodes
{
    public const int CustomData = 53;
    public const int Sync = 190;

    private static readonly string[] Names =
    [
        "add", "and", "break", "breakc", "call", "callc", "case", "continue", "continuec", "cut",
        "default", "deriv_rtx", "deriv_rty", "discard", "div", "dp2", "dp3", "dp4", "else", "emit",
        "emit_then_cut", "endif", "endloop", "endswitch", "eq", "exp", "frc", "ftoi", "ftou", "ge",
        "iadd", "if", "ieq", "ige", "ilt", "imad", "imax", "imin", "imul", "ine",
        "ineg", "ishl", "ishr", "itof", "label", "ld", "ld_ms", "log", "loop", "lt",
        "mad", "min", "max", "customdata", "mov", "movc", "mul", "ne", "nop", "not",
        "or", "resinfo", "ret", "retc", "round_ne", "round_ni", "round_pi", "round_z", "rsq", "sample",
        "sample_c", "sample_c_lz", "sample_l", "sample_d", "sample_b", "sqrt", "switch", "sincos", "udiv", "ult",
        "uge", "umul", "umad", "umax", "umin", "ushr", "utof", "xor", "dcl_resource", "dcl_constantbuffer",
        "dcl_sampler", "dcl_indexrange", "dcl_outputtopology", "dcl_inputprimitive", "dcl_maxout", "dcl_input", "dcl_input_sgv", "dcl_input_siv", "dcl_input_ps", "dcl_input_ps_sgv",
        "dcl_input_ps_siv", "dcl_output", "dcl_output_sgv", "dcl_output_siv", "dcl_temps", "dcl_indexableTemp", "dcl_globalFlags", "reserved0",
        "lod", "gather4", "sample_pos",
        "sample_info", "reserved1", "hs_decls", "hs_control_point_phase", "hs_fork_phase", "hs_join_phase", "emit_stream", "cut_stream", "emit_then_cut_stream", "fcall",
        "bufinfo", "deriv_rtx_coarse", "deriv_rtx_fine", "deriv_rty_coarse", "deriv_rty_fine", "gather4_c", "gather4_po", "gather4_po_c", "rcp", "f32tof16",
        "f16tof32", "uaddc", "usubb", "countbits", "firstbit_hi", "firstbit_lo", "firstbit_shi", "ubfe", "ibfe", "bfi",
        "bfrev", "swapc", "dcl_stream", "dcl_function_body", "dcl_function_table", "dcl_interface", "dcl_input_control_point_count", "dcl_output_control_point_count", "dcl_tessellator_domain", "dcl_tessellator_partitioning",
        "dcl_tessellator_output_primitive", "dcl_hs_max_tessfactor", "dcl_hs_fork_phase_instance_count", "dcl_hs_join_phase_instance_count", "dcl_thread_group", "dcl_uav_typed", "dcl_uav_raw", "dcl_uav_structured", "dcl_tgsm_raw", "dcl_tgsm_structured",
        "dcl_resource_raw", "dcl_resource_structured", "ld_uav_typed", "store_uav_typed", "ld_raw", "store_raw", "ld_structured", "store_structured", "atomic_and", "atomic_or",
        "atomic_xor", "atomic_cmp_store", "atomic_iadd", "atomic_imax", "atomic_imin", "atomic_umax", "atomic_umin", "imm_atomic_alloc", "imm_atomic_consume", "imm_atomic_iadd",
        "imm_atomic_and", "imm_atomic_or", "imm_atomic_xor", "imm_atomic_exch", "imm_atomic_cmp_exch", "imm_atomic_imax", "imm_atomic_imin", "imm_atomic_umax", "imm_atomic_umin", "sync",
        "dadd", "dmax", "dmin", "dmul", "deq", "dge", "dlt", "dne", "dmov", "dmovc",
        "dtof", "ftod", "eval_snapped", "eval_sample_index", "eval_centroid", "dcl_gs_instance_count", "abort", "debug_break",
    ];

    public static string NameOf(int opCode) => opCode >= 0 && opCode < Names.Length ? Names[opCode] : $"opcode_{opCode}";

    public static bool IsDeclaration(int opCode) => opCode is >= 88 and <= 106 or >= 143 and <= 162 or 206;

    public static bool HasSaturate(int opCode) => !IsDeclaration(opCode) && opCode != Sync && opCode != CustomData;

    public static int OperandCountOf(int opCode, uint token) => opCode switch
    {
        88 or 89 or 90 or 91 => 1,
        92 or 93 or 94 => 0,
        >= 95 and <= 103 => 1,
        104 or 105 or 106 => 0,
        143 => 1,
        >= 144 and <= 155 => 0,
        >= 156 and <= 162 => 1,
        206 => 0,
        _ => int.MaxValue,
    };
}
