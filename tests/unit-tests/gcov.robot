*** Settings ***
Suite Setup                   Setup
Suite Teardown                Teardown
Test Setup                    Reset Emulation
Test Teardown                 Test Teardown
Resource                      ${RENODEKEYWORDS}

*** Keywords ***
Prepare Machine
    Execute Command           using sysbus
    Execute Command           mach create
    Execute Command           machine LoadPlatformDescription @platforms/cpus/kendryte_k210.repl

    Execute Command           machine SetSerialExecution True

    Execute Command           sysbus Tag <0x50440000 0x10000> "SYSCTL"
    Execute Command           sysbus Tag <0x50440018 0x4> "pll_lock" 0xFFFFFFFF
    Execute Command           sysbus Tag <0x5044000C 0x4> "pll1"
    Execute Command           sysbus Tag <0x50440008 0x4> "pll0"
    Execute Command           sysbus Tag <0x50440020 0x4> "clk_sel0"
    Execute Command           sysbus Tag <0x50440028 0x4> "clk_en_cent"
    Execute Command           sysbus Tag <0x5044002c 0x4> "clk_en_peri"

    Execute Command           sysbus LoadELF @${CURDIR}/addr2line-test1.elf

*** Test Cases ***
Should Run
    Prepare Machine
    Execute Command           cpu2 MaximumBlockSize 1
    Execute Command           cpu1 MaximumBlockSize 1

    Execute Command           gcov start @${CURDIR}/addr2line-test1.elf
    Execute Command           emulation RunFor "0.007"

    Execute Command           gcov stop @${CURDIR}/gcov_results

    ${g}=  Run Process        gcov-10  gcov_results.gc  -s  builds/git/renode-demo-sources/repositories/kendryte-standalone-sdk/src/test1  -t  cwd=${CURDIR}
    Should Contain            ${g.stdout}      101:${SPACE*3} 8:
    Should Contain            ${g.stdout}       10:${SPACE*3}19:
    Should Contain            ${g.stdout}        1:${SPACE*3}22:

Should Map Lines
    Prepare Machine

    ${x}=  Execute Command     gcov func2lines @${CURDIR}/addr2line-test1.elf "main"
    Should Contain             ${x}  main.c:13|12

    ${x}=  Execute Command     gcov func2lines @${CURDIR}/addr2line-test1.elf "buf_increment_all"
    Should Contain             ${x}  main.c:7|34

