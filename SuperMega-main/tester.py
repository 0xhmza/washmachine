from typing import Dict, List
import sys
import os

from helper import *
from config import config
from model.defs import *
from model.settings import Settings
from log import setup_logging
from supermega import start
from utils import check_deps


def main():
    print("Super Mega Tester: " + os.path.dirname(VerifyFilename))

    #setup_logging(level=logging.INFO)
    setup_logging(level=logging.WARNING)

    config.load()
    check_deps()

    if not os.path.exists(os.path.dirname(VerifyFilename)):
        print("{} directory does not exist".format(os.path.dirname(VerifyFilename)))
        return
    
    if len(sys.argv) < 2:
        print("Usage: python tester.py <test>")
        print("Available tests: all, common, dll_loader, exe_code, exe_data, dll_code, dll_data")
        return
    
    match sys.argv[1]:
        case "all":
            test_common()
            
            test_exe_data()
            test_exe_code()

            test_dll_code()
            test_dll_data()

            test_dll_loader()

        case "common":
            test_common()
        case "dll_loader":
            test_dll_loader()
        case "exe_code":
            test_exe_code()
        case "exe_data":
            test_exe_data()
        case "dll_code":
            test_dll_code()
        case "dll_data":
            test_dll_data()
        case _:
            print("Unknown test: {}".format(sys.argv[1]))
            print("Available tests: all, common, dll_loader, exe_code, exe_data, dll_code, dll_data")
            return


def test_common():
    print("Testing: COMMON procexp64.exe, alloc_rw_rwx, PayloadLocation.DATA, BackdoorFunc")

    settings = Settings("unittest")
    settings.injectable_base = "procexp64.exe"
    settings.payload_base = "createfile.bin"
    settings.payload_location = PayloadLocation.DATA
    settings.carrier_name = "alloc_rw_rwx" # important (not rx)
    settings.carrier_invoke_style = CarrierInvokeStyle.BackdoorFunc

    settings.verify = True
    settings.try_start_final_infected_exe = False
    settings.cleanup_files_on_exit = False
    
    print("Test COMMON 1/6: plain")
    settings.decoder_style = "plain"
    if not start(settings):
        return

    print("Test COMMON 2/6: xor_1")
    settings.decoder_style = "xor_1"
    if not start(settings):
        return

    print("Test COMMON 3/6: xor_2")
    settings.decoder_style = "xor_2"
    if not start(settings):
        return

    print("Test COMMON 4/6: +guardrail env")
    settings.plugin_guardrail = "env"
    settings.plugin_guardrail_data_key = "VCIDEInstallDir"
    settings.plugin_guardrail_data_value = "Community"
    if not start(settings):
        return

    print("Test COMMON 5/6: +sirallocalot ")
    settings.plugin_antiemulation = "sirallocalot"
    if not start(settings):
        return

    print("Test COMMON 6/6: +virtualprotect undersized")
    settings.plugin_virtualprotect = "undersized"
    if not start(settings):
        return


def test_exe_data():
    print("Testing EXE: Payload in .data")
    settings = Settings("unittest")

    settings.payload_base = "createfile.bin"
    settings.verify = True
    settings.try_start_final_infected_exe = False
    settings.payload_location = PayloadLocation.DATA
    settings.carrier_name = "alloc_rw_rwx"  # important (not rx)


    # EXE: PROCEXP
    settings.injectable_base = "procexp64.exe"

    print("Test EXE DATA 1/8: procexp, overwrite-main")
    settings.carrier_invoke_style = CarrierInvokeStyle.OverwriteFunc
    if not start(settings):
        return
    
    print("Test EXE DATA 2/8: procexp, backdoor-main")
    settings.carrier_invoke_style = CarrierInvokeStyle.BackdoorFunc
    if not start(settings):
        return


    # EXE: 7Z
    settings.injectable_base = "7z.exe"
        
    print("Test EXE DATA 5/8: 7z, overwrite-main")
    settings.carrier_invoke_style = CarrierInvokeStyle.OverwriteFunc
    if not start(settings):
        return

    print("Test EXE DATA 6/4: 7z, backdoor-main")
    settings.carrier_invoke_style = CarrierInvokeStyle.BackdoorFunc
    if not start(settings):
        return
    

def test_exe_code():
    print("Testing: EXEs: Payload in .text")
    settings = Settings("unittest")
    
    settings.payload_base = "createfile.bin"
    settings.verify = True
    settings.try_start_final_infected_exe = False
    settings.payload_location = PayloadLocation.CODE
    settings.carrier_name = "alloc_rw_rwx"  # important (not rx)
    
    # EXE 7Z
    settings.injectable_base = "7z.exe"

    print("Test EXE CODE 1/8: 7z, overwrite-main")
    settings.carrier_invoke_style = CarrierInvokeStyle.OverwriteFunc
    if not start(settings):
        return

    print("Test EXE CODE 2/8: 7z, backdoor-main")
    settings.carrier_name = "peb_walk"
    settings.carrier_invoke_style = CarrierInvokeStyle.BackdoorFunc
    if not start(settings):
        return
    

    # EXE procexp64.exe

    settings.injectable_base = "procexp64.exe"

    print("Test EXE CODE 5/8: procexp, overwrite-main")
    settings.carrier_name = "alloc_rw_rwx"
    settings.carrier_invoke_style = CarrierInvokeStyle.OverwriteFunc
    if not start(settings):
        return

    print("Test EXE CODE 6/8: procexp, backdoor-main")
    settings.carrier_name = "alloc_rw_rwx"
    settings.carrier_invoke_style = CarrierInvokeStyle.BackdoorFunc
    if not start(settings):
        return



def test_dll_code():
    print("Testing: DLLs code")
    settings = Settings("unittest")
    settings.injectable_base = "libbz2.dll"
    settings.payload_base = "createfile.bin"
    settings.verify = True
    settings.try_start_final_infected_exe = False
    settings.payload_location = PayloadLocation.CODE
    
    print("Test DLL 1/4: libbz2.dll, peb-walk, overwrite-func dllMain (func=None)")
    settings.carrier_name = "peb_walk"
    settings.carrier_invoke_style = CarrierInvokeStyle.OverwriteFunc
    if not start(settings):
        return

    print("Test DLL 2/4: libbz2.dll, peb-walk, hijack dllMain (func=None)")
    settings.carrier_name = "peb_walk"
    settings.carrier_invoke_style = CarrierInvokeStyle.BackdoorFunc
    if not start(settings):
        return

    print("Test DLL 3/4: libbz2.dll, peb-walk, overwrite-func, func=BZ2_bzDecompress")
    settings.dllfunc = "BZ2_bzDecompressInit"
    settings.carrier_name = "peb_walk"
    settings.carrier_invoke_style = CarrierInvokeStyle.OverwriteFunc
    if not start(settings):
        return

    print("Test DLL 4/4: libbz2.dll, peb-walk, hijack main, func=BZ2_bzdopen")
    settings.dllfunc = "BZ2_bzdopen"
    settings.carrier_name = "peb_walk"
    settings.carrier_invoke_style = CarrierInvokeStyle.BackdoorFunc
    if not start(settings):
        return


def test_dll_data():
    print("Testing: DLLs data")
    settings = Settings("unittest")
    settings.injectable_base = "libbz2.dll"
    settings.payload_base = "createfile.bin"
    settings.verify = True
    settings.try_start_final_infected_exe = False
    settings.payload_location = PayloadLocation.DATA
    settings.carrier_name = "peb_walk"
    ###########settings.fix_missing_iat = True

    # func = ""
    
    print("Test DLL 1/4: libbz2.dll, overwrite-dllMain")
    settings.carrier_invoke_style = CarrierInvokeStyle.OverwriteFunc
    if not start(settings):
        return

    print("Test DLL 1/4: libbz2.dll, backdoor-dllMain")
    settings.carrier_invoke_style = CarrierInvokeStyle.BackdoorFunc
    if not start(settings):
        return
    

    # func = "BZ2_bzDecompressInit"
    settings.dllfunc = "BZ2_bzDecompressInit"

    print("Test DLL 3/4: libbz2.dll, overwrite=BZ2_bzDecompress")
    settings.carrier_invoke_style = CarrierInvokeStyle.OverwriteFunc
    if not start(settings):
        return

    print("Test DLL 4/4: libbz2.dll, backdoor=BZ2_bzDecompress")
    settings.carrier_invoke_style = CarrierInvokeStyle.BackdoorFunc
    if not start(settings):
        return
    
    
def test_dll_loader():
    print("Testing: DLL Loader")
    settings = Settings("unittest")
    settings.injectable_base = "procexp64.exe"
    settings.payload_base = "createfile.dll"

    settings.verify = True
    settings.try_start_final_infected_exe = False
    settings.payload_location = PayloadLocation.CODE   # important
    settings.carrier_invoke_style = CarrierInvokeStyle.ChangeEntryPoint

    print("Test DLL Loader 1/2: procexp, backdoor main, dll loader alloc")
    settings.carrier_name = "dll_loader_alloc"
    if not start(settings):
        return

    print("Test DLL Loader 2/2: procexp, backdoor main, dll loader change")
    settings.carrier_name = "dll_loader_change"
    if not start(settings):
        return
    

if __name__ == "__main__":
    main()
