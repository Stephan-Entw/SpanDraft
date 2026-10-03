#!/usr/bin/env python3
"""Explicit external/analytical regeneration into a NEW candidate directory."""
import argparse
import os
import sys
from pathlib import Path

os.environ.setdefault('MPLCONFIGDIR','/tmp/spandraft-validation-matplotlib')
os.environ.setdefault('MPLBACKEND','Agg')

from common import ROOT, check_environment, load_case, write_result
import analytical
import indeterminatebeam_adapter
import pycba_adapter


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=ROOT/'validation/results/candidate-references')
    parser.add_argument('--case',action='append',help='Generate only selected case IDs (repeatable)')
    parser.add_argument('--solver',choices=['analytical','indeterminatebeam','pycba'],action='append')
    args=parser.parse_args()
    check_environment()
    output=args.output.resolve()
    goldens=(ROOT/'validation/references').resolve()
    if output==goldens or goldens in output.parents:
        parser.error('Generate candidates first; use promote_references.py for reviewed golden changes')
    files=sorted((ROOT/'validation/cases').glob('*.json'))
    if args.case:
        known={f.stem for f in files}
        if not set(args.case)<=known: parser.error('Unknown case ID')
        files=[f for f in files if f.stem in args.case]
    modules={'indeterminatebeam':indeterminatebeam_adapter,'pycba':pycba_adapter,'analytical':analytical}
    failed=False
    for path in files:
        case=load_case(path)
        for solver in args.solver or ['indeterminatebeam','pycba','analytical']:
            if solver=='analytical' and case['caseId'].split('-')[0] not in ['V01','V02','V03','V04','V05','V06']:
                continue
            target=output/(case['caseId']+'.'+solver+'.json')
            if target.exists(): raise FileExistsError(f'Refusing overwrite: {target}')
            data=modules[solver].generate(case)
            write_result(target,data)
            print(f"{case['caseId']} {solver}: {data['status']}"+
                  (f" npts={data['provenance']['options']['npts']}" if solver=='pycba' else ''),flush=True)
            failed=failed or data['status']!='PASS'
    return 1 if failed else 0


if __name__=='__main__': sys.exit(main())
