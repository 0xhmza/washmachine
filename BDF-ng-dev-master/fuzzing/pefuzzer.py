#!/usr/bin/env python3

import io
import afl
afl.init()

import os, sys
curdir = os.path.dirname(os.path.realpath(__file__))
prntdir = os.path.dirname(curdir)
sys.path.append(prntdir)

from pe import pe_parse

target_file = io.BytesIO(open(sys.argv[1], 'r+b').read())

parser = pe_parse.pe_parse(FILE=target_file, 
                                      DISK_OFFSET=0
                                      )

if not parser.run():
    raise

os._exit(0)

