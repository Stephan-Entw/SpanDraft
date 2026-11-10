"""Targeted limit-case configurations shared by targeted.py, ablation.py and invariance.py."""
from gen import case, INF

L, P, E, I = 4., -10000., 210e9, 8e-6
EI = E * I


def configs(g, k, kt):
    m = L / 2
    return dict(
        paar=dict(sup=[(0, 'Pinned'), (L, 'Roller')], springs=[(m, k, 0, False), (m + g, k, 0, False)]),
        nebenlager=dict(sup=[(0, 'Pinned'), (m, 'Roller'), (L, 'Roller')], springs=[(m + g, k, 0, False)]),
        nebengelenk=dict(sup=[(0, 'Fixed'), (L, 'Roller')], hinges=[m], springs=[(m + g, k, 0, False)]),
        nurfedern=dict(sup=[], springs=[(0, k, 0, True), (g, k, 0, False), (L, k, 0, False)]),
        teileinsp=dict(sup=[(g, 'Roller'), (L, 'Roller')], springs=[(0, INF, kt, True)]),
        elasteinsp=dict(sup=[(0, 'Pinned')], hinges=[L - g], springs=[(L, k, kt, False)]),
        amgelenk=dict(sup=[(0, 'Fixed')], hinges=[m], springs=[(m, k, 0, False), (m + g, k, 0, False), (L, k, 0, False)]),
        drei=dict(sup=[(0, 'Pinned'), (L, 'Roller')], springs=[(m + i * g, k, 0, False) for i in range(3)]),
        acht=dict(sup=[(0, 'Pinned'), (L, 'Roller')], springs=[(m + i * g, k, 0, False) for i in range(8)]),
        drehpaar=dict(sup=[(0, 'Pinned'), (L, 'Roller')], springs=[(m, k, kt, False), (m + g, k, kt, False)]),
        drehacht=dict(sup=[(0, 'Pinned'), (L, 'Roller')], springs=[(m + i * g, k, kt, False) for i in range(8)]),
        gemischt=dict(sup=[(0, 'Pinned'), (m, 'Roller'), (m + 2 * g, 'Roller'), (L, 'Roller')],
                      springs=[(m + g, k, 0, False), (m + 3 * g, INF, kt, False)]),
        federgelenkfeder=dict(sup=[(0, 'Fixed'), (L, 'Roller')], hinges=[m], springs=[(m - g, k, 0, False), (m + g, k, 0, False)]),
        gelenkcluster=dict(sup=[(0, 'Fixed'), (L, 'Fixed')], hinges=[m, m + 2 * g], springs=[(m + g, k, kt, False)]),
        gelenkneblager=dict(sup=[(0, 'Fixed'), (m, 'Roller'), (L, 'Fixed')], hinges=[m + g]),
        lagerpaar=dict(sup=[(0, 'Pinned'), (m, 'Roller'), (m + g, 'Roller'), (L, 'Roller')]),
        lagerdrei=dict(sup=[(0, 'Pinned'), (m, 'Roller'), (m + g, 'Roller'), (m + 2 * g, 'Roller'), (L, 'Roller')]),
        lagerfuenf=dict(sup=[(0, 'Pinned')] + [(m + i * g, 'Roller') for i in range(5)] + [(L, 'Fixed')]),
        federaneinsp=dict(sup=[(0, 'Pinned'), (L, 'Fixed')], springs=[(L - g, INF, kt, False)]),
        eiimcluster=dict(sup=[(0, 'Pinned'), (L, 'Roller')], springs=[(m, k, 0, False), (m + 2 * g, k, 0, False)],
                         ei=[(m + g, 100 * EI), (m + 3 * g, EI)]),
        eiamlager=dict(sup=[(0, 'Pinned'), (m, 'Roller'), (L, 'Roller')], springs=[(m + g, k, 0, False)],
                       ei=[(m, EI / 50), (m + g, 3 * EI)]),
        kurzlastvz=dict(sup=[(0, 'Pinned'), (L, 'Roller')], springs=[(m, k, 0, False), (m + g, k, 0, False)],
                        lin=[(m - g, m + 2 * g, 5e4, -5e4), (m + g / 4, m + 3 * g / 4, -3e5, 3e5)]),
        # coincident positions (sub-points) in the order defined by transfer_ref
        gelenkauflager=dict(sup=[(0, 'Fixed'), (m, 'Roller'), (L, 'Fixed')], hinges=[m], springs=[(m + g, k, 0, False)]),
        kraftaufgelenk=dict(sup=[(0, 'Fixed'), (L, 'Roller')], hinges=[m], springs=[(m + g, k, 0, False)], pfx=[(m, 3 * P)]),
        kraftauffeder=dict(sup=[(0, 'Pinned'), (L, 'Roller')], springs=[(m, k, kt, False), (m + g, k, 0, False)], pfx=[(m, 2 * P)], pmx=[(m + g, 900.)]),
        eiaufgelenk=dict(sup=[(0, 'Fixed'), (L, 'Fixed')], hinges=[m], springs=[(m + g, k, 0, False)], ei=[(m, EI / 7)]),
        eiauffeder=dict(sup=[(0, 'Pinned'), (L, 'Roller')], springs=[(m, k, kt, False), (m + g, k, 0, False)], ei=[(m, 9 * EI)]),
    )


def build(nm, t, r, u, g):
    """Case dict of configuration nm with the standard loads; None if a position leaves the beam."""
    xs = [a for a, _ in t['sup']] + [s[0] for s in t.get('springs', [])] + t.get('hinges', []) + [x for x, _ in t.get('ei', [])]
    if any(not 0 <= x <= L for x in xs):
        return None
    x0 = t['springs'][0][0] if t.get('springs') else L / 2
    pf = [(.3 * L, P), (.8 * L, P), (min(x0 + g / 2, L - g / 2), P)] + t.get('pfx', [])
    pm = [(L / 4, 1500.)] + t.get('pmx', [])
    return case(f'{nm}|{r}|{u}', L, t['sup'], springs=t.get('springs', []), hinges=t.get('hinges', []), pf=pf, pm=pm,
                q=[(0., L, -2000.)], E=E, I=I, ei=t.get('ei', []), lin=t.get('lin', []))


def all_cases(distances=(1e-1, 1e-3, 1e-5, 1e-7, 1e-9, 1e-11), exponents=(-3, 0, 3, 6, 9, 12), names=None):
    base = 0 if 0 in exponents else exponents[0]   # configurations without springs once per distance
    for r in distances:
        g = r * L
        for u in exponents:
            k, kt = 10 ** u * EI / L ** 3, 10 ** u * EI / L
            for nm, t in configs(g, k, kt).items():
                if names and nm not in names:
                    continue
                if not t.get('springs') and u != base:
                    continue
                c = build(nm, t, r, u, g)
                if c is not None:
                    yield nm, c
