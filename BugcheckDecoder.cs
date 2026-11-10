namespace CrashWatch;

/// <summary>Maps Windows bugcheck codes to human-readable names and decodes the P-parameters.</summary>
internal static class BugcheckDecoder
{
    public static (string Name, string Detail) Decode(int code, ulong p1, ulong p2, ulong p3, ulong p4)
    {
        if (code == 0)
        {
            return ("Unexpected shutdown",
                "No bugcheck was recorded — the system froze or lost power (no dump written).");
        }

        string name = code switch
        {
            0x0A  => "IRQL_NOT_LESS_OR_EQUAL",
            0x1A  => "MEMORY_MANAGEMENT",
            0x19  => "BAD_POOL_HEADER",
            0x1E  => "KMODE_EXCEPTION_NOT_HANDLED",
            0x3B  => "SYSTEM_SERVICE_EXCEPTION",
            0x50  => "PAGE_FAULT_IN_NONPAGED_AREA",
            0x7F  => "UNEXPECTED_KERNEL_MODE_TRAP",
            0x9F  => "DRIVER_POWER_STATE_FAILURE",
            0xC2  => "BAD_POOL_CALLER",
            0xC5  => "DRIVER_CORRUPTED_EXPOOL",
            0xC9  => "DRIVER_VERIFIER_IOMANAGER_VIOLATION",
            0xCE  => "DRIVER_UNLOADED_WITHOUT_CANCELLING_PENDING_OPERATIONS",
            0xD1  => "DRIVER_IRQL_NOT_LESS_OR_EQUAL",
            0xEF  => "CRITICAL_PROCESS_DIED",
            0x101 => "CLOCK_WATCHDOG_TIMEOUT",
            0x109 => "CRITICAL_STRUCTURE_CORRUPTION",
            0x116 => "VIDEO_TDR_FAILURE",
            0x117 => "VIDEO_TDR_TIMEOUT_DETECTED",
            0x124 => "WHEA_UNCORRECTABLE_ERROR",
            0x133 => "DPC_WATCHDOG_VIOLATION",
            0x13A => "KERNEL_MODE_HEAP_CORRUPTION",
            0x13C => "INVALID_KERNEL_STACK_ADDRESS",
            0x139 => "KERNEL_SECURITY_CHECK_FAILURE",
            _     => $"BUGCHECK_0x{code:X}",
        };

        string detail = code switch
        {
            0x0A  => $"A driver touched {Ptr(p1)} at IRQL {p2}{(p2 == 2 ? " (DISPATCH_LEVEL)" : "")}. Usually a driver bug or memory corruption.",
            0x1A  => "The memory manager found corruption. Often a failing RAM module or bad IMC.",
            0x19  => "The pool header was corrupted — kernel memory corruption.",
            0x3B  => $"Access violation ({Ptr(p1)}) inside a system service. Often memory corruption or a bad driver.",
            0x50  => $"Page fault at {Ptr(p1)} in non-paged memory. Often memory corruption.",
            0x7F  => p1 == 8
                     ? "DOUBLE FAULT — an exception occurred while handling an exception. Kernel stack/memory corruption; no driver blamed."
                     : $"Trap {p1}.",
            0x9F  => "A driver blocked a power transition (sleep/shutdown) — driver bug.",
            0xD1  => $"A driver touched {Ptr(p1)} at IRQL {p2}{(p2 == 2 ? " (DISPATCH_LEVEL)" : "")}. Classic driver bug on an I/O error path.",
            0x101 => "A core stopped responding to clock interrupts — hang or hardware instability.",
            0x109 => "Kernel structures were corrupted. Often memory corruption or a bad driver.",
            0x116/0x117 => "The GPU driver stopped responding (TDR).",
            0x124 => HardwareDetail(p1, p2, p3, p4),
            0x13A => "The kernel heap was corrupted — something wrote past its allocation.",
            0x139 => "A kernel security check (stack cookie / bounds) failed. Memory corruption.",
            _     => $"P1={Ptr(p1)} P2={Ptr(p2)} P3={Ptr(p3)} P4={Ptr(p4)}",
        };

        return (name, detail);
    }

    private static string HardwareDetail(ulong p1, ulong p2, ulong p3, ulong p4)
    {
        ulong status = (p3 << 32) | (p4 & 0xFFFFFFFF);
        bool val = (status >> 63) != 0;
        bool uc  = (status >> 61) != 0;
        ulong mca = status & 0xFFFF;
        string source = p1 switch
        {
            0 => "machine check exception (MCE)",
            1 => "corrected machine check",
            2 => "NMI",
            3 => "PCI Express error",
            _ => "hardware error",
        };
        return $"Uncorrected {source}. MCi_STATUS=0x{status:X16}{(val ? $" (MCA code 0x{mca:X4}, {(uc ? "uncorrected" : "corrected")})" : "")}. " +
               "Points at CPU/IMC/cache — check cooling and memory stability.";
    }

    private static string Ptr(ulong v) => v == 0 ? "a null pointer" : $"0x{v:X}";
}
