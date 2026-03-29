from flask import Flask, Blueprint, current_app, request, redirect, url_for, render_template, send_file, make_response, session, jsonify
from werkzeug.utils import secure_filename
import os
import logging
from typing import List, Tuple

from pe.superpe import SuperPe
from model.defs import *
from pe.dllresolver import resolve_dlls

views = Blueprint('views', __name__)
logger = logging.getLogger("Views")


@views.route("/")
def index():
    return render_template('index.html')


@views.route("/injectables/<exe_name>")
def injectable_view(exe_name):
    filepath = "{}{}".format(PATH_INJECTABLES, exe_name)
    if not os.path.exists(filepath):
       return "File not found: {}".format(exe_name)

    superpe = SuperPe(filepath)

    return render_template('injectable.html', 
                           superpe=superpe, 
                           resolved_dlls=resolve_dlls(superpe),
                           iat=superpe.get_iat_entries(),
                           exports=superpe.get_exports_full(),
    )


@views.route("/injectables")
def injectables_view():
    injectables = []
    for file in os.listdir(PATH_INJECTABLES):
        if not file.endswith(".dll") and not file.endswith(".exe"):
            continue
        if '.verify' in file or '.test' in file:
            continue

        superpe = SuperPe("{}/{}".format(PATH_INJECTABLES, file))

        e = {
            'name': file,
            #'exports': superpe.get_exports_full(),
            #'iat': superpe.get_iat_entries(),
            'superpe': superpe,
            #'sections': superpe.pe_sections,
        }
        injectables.append(e)
        #break
    return render_template('injectables.html', 
                           injectables=injectables)


@views.app_template_filter('hexint')
def hex_filter(s):
    return hex(s)

@views.app_template_filter('basename')
def basename(s):
    return os.path.basename(s)