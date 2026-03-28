import struct
import logging
import hashlib
import re
import collections
import ast
logger = logging.getLogger(__name__)


def pack_ip_addresses(HOST):
    # update this to support 192.168.0.1 => 192.168.1
    hostocts = []
    try:
        for i, octet in enumerate(HOST.split('.')):
                hostocts.append(int(octet))
        hostip = struct.pack('=BBBB', hostocts[0], hostocts[1],
                                  hostocts[2], hostocts[3])
    except Exception as e:
        logger.error(f"{Exception}: {e}")
        return False

    return hostip 


def hashit(algo, aFile):
    BLOCKSIZE = 4096
    m = getattr(hashlib, algo)()

    buf = aFile.read(BLOCKSIZE)
    while len(buf) > 0:
        m.update(buf)
        buf = aFile.read(BLOCKSIZE)

    aFile.seek(0)

    return m.hexdigest()


def save_patch_instr(self):
    # Just say no to pickles
    patch_instr_name = (self.file_base_name + '_' + self.original_file_hash + '_' +
                        self.options['PATCH_METHOD'] + '_' + str(self.found_method.found_mode.name) +
                        '_' + str(self.found_payload.name))
    if 'PORT' in dir(self.found_payload):
        patch_instr_name += '_' + str(self.found_payload.PORT)

    if 'HOST' in dir(self.found_payload):
        patch_instr_name += '_' + str(self.found_payload.HOST)

    patch_instr_name += ".patch"
    with open(patch_instr_name, 'w') as f:
        f.write(str(self.patch_instr))


def return_patch_instr(patchInstr):
    try:
        with open(patchInstr, 'r') as f:
            a = f.read()
            m = re.match(r'^OrderedDict\((.+)\)$', a)
            if m:
                return collections.OrderedDict(ast.literal_eval(m.group(1)))
    except Exception as e:
        logger.error(f"[!] Exception: {Exception}, {e}")
        return False


def hashbytes(algo, buf):
    m = getattr(hashlib, algo)()
    m.update(buf)
    return m.hexdigest()
