#!/usr/bin/env python3
"""Deliberate golden promotion. Never called by tests or CI."""
import argparse
import json
import shutil
from pathlib import Path
from common import ROOT

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--from',dest='source',type=Path,required=True)
parser.add_argument('--reviewed',action='store_true',help='Acknowledge review of provenance and comparison reports')
args=parser.parse_args()
if not args.reviewed: parser.error('--reviewed is required; inspect differences and comparison reports first')
source=args.source.resolve()
target=ROOT/'validation/references'
if source==target.resolve(): parser.error('Source must be a candidate directory')
files=list(source.glob('*.json'))
if not files: parser.error('No candidate files')
# Validate every file before performing any writes. FAIL/PARTIAL are retained as evidence;
# their presence causes normal acceptance tests to fail, never a reduced acceptance scope.
for file in files:
    data=json.loads(file.read_text())
    if file.name!=data['caseId']+'.'+data['solver']+'.json' or data['schemaVersion']!=1:
        parser.error('Invalid candidate identity: '+file.name)
for file in files:
    shutil.copyfile(file,target/file.name)
print(f'Promoted {len(files)} references. Inspect git diff; no automatic commit was made.')
