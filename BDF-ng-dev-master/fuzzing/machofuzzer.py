#!/usr/bin/env python3

import io
import afl
afl.init()

import os
import sys
curdir = os.path.dirname(os.path.realpath(__file__))
prntdir = os.path.dirname(curdir)
sys.path.append(prntdir)

from macho import macho_parse

target_file = io.BytesIO(open(sys.argv[1], 'r+b').read())

parser = macho_parse.macho_parse(FILE=target_file)

if not parser.run():
    raise

os._exit(0)
