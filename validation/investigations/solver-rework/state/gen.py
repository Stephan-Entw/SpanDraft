"""Model generators, ported from ../s_fuzz.py (gen) and ../f_fuzz.py (gen) without numpy.

gen_s draws the same random stream as s_fuzz.gen (identical models for equal seeds). gen_f draws
the same stream as f_fuzz.gen including the imposed-curvature draws, which are then discarded
(decision 6: temperature curvature is not part of the rework). Both return case dicts for
../transfer_ref.py.
"""
INF = 'inf'


def pts(c):
    L = c['L']
    ev = sorted({0.0, L} | {s[0] for s in c['sup']} | {s[0] for s in c.get('springs', [])} | {a for a, _ in c['pf']}
                | {a for a, _ in c['pm']} | {x for a, b, _ in c['q'] for x in (a, b)}
                | {x for a, b, *_ in c.get('lin', []) for x in (a, b)} | set(c.get('hinges', []))
                | {x for x, _ in c.get('ei', [])})
    out = []
    for j, x in enumerate(ev):
        if j > 0:
            out.append([x, 'L'])
        if j < len(ev) - 1:
            out.append([x, 'R'])
            for f in (.3, .7):
                out.append([x + (ev[j + 1] - x) * f, 'R'])
    return out


def case(id, L, sup, springs=(), hinges=(), pf=(), pm=(), q=(), E=210e9, I=None, ei=(), lin=()):
    I = I if I is not None else 8e-6 * L ** 4 / 4 ** 4
    c = dict(id=id, L=L, E=E, A=.003 * L * L / 16, I=I, sup=[[a, t] for a, t in sup], springs=[list(s) for s in springs],
             hinges=list(hinges), pf=[list(p) for p in pf], pm=[list(p) for p in pm], q=[list(p) for p in q],
             ei=[list(e) for e in ei], lin=[list(l) for l in lin], curv=[])
    c['pts'] = pts(c)
    return c


def gen_s(rng, r0, id='s'):
    L = 10 ** rng.uniform(-2, 1.5); E = rng.choice([210e9, 70e9, 2.5e9]); I = L ** 4 * 10 ** rng.uniform(-9, -5); EI = E * I
    while True:
        k = rng.randint(1, 6); grid = sorted(rng.sample(range(0, 21), k)); sup = []; hinges = []; spr = []
        for gq in grid:
            x = L if gq == 20 else gq * L / 20; u = rng.random()
            if u < .25: sup.append((x, 'Roller'))
            elif u < .35: sup.append((x, 'Pinned'))
            elif u < .45: sup.append((x, 'Fixed'))
            elif u < .6 and 0 < x < L: hinges.append(x)
            else:
                kw = INF if rng.random() < .15 else 10 ** rng.uniform(-4, 12) * EI / L ** 3
                v = rng.random(); kt = 0 if v < .5 else (INF if v < .65 else 10 ** rng.uniform(-4, 12) * EI / L)
                if kw == INF and kt == 0: kt = 10 ** rng.uniform(-4, 12) * EI / L
                spr.append((x, kw, kt, rng.random() < .5))
        if not spr: continue
        anchors = [a for a, _ in sup] + [s[0] for s in spr] + hinges
        spr = [((rng.choice(anchors) + rng.choice([-1, 1]) * r0 * L) if rng.random() < .5 else s[0],) + tuple(s[1:]) for s in spr]
        spr = [s for s in spr if 0 <= s[0] <= L]
        hinges = sorted({(rng.choice(anchors) + rng.choice([-1, 1]) * r0 * L) if rng.random() < .4 else h for h in hinges})
        hinges = [h for h in hinges if 0 < h < L]
        if len(set(s[0] for s in spr)) < len(spr) or any(s[0] in [a for a, _ in sup] for s in spr): continue
        if any(h == a and t == 'Fixed' for h in hinges for a, t in sup) or any(h == s[0] and s[2] != 0 for h in hinges for s in spr): continue
        if not spr: continue
        break
    mag = 10 ** rng.uniform(1, 5)
    allx = [a for a, _ in sup] + [s[0] for s in spr] + hinges
    pos = lambda: rng.choice([rng.uniform(0, L), rng.choice(allx) + rng.choice([-1, 1]) * r0 * L])
    pf = [(x, -mag * rng.uniform(.2, 1)) for x in (pos() for _ in range(rng.randint(1, 4))) if 0 <= x <= L]
    pm = [(x, mag * L * rng.uniform(-.3, .3)) for x in (pos() for _ in range(rng.randint(0, 2))) if 0 <= x <= L and x not in hinges]
    q = [(0., L, -mag / L)] if rng.random() < .3 else []
    if not pf: pf = [(L * .51, -mag)]
    return case(id, L, sup, springs=spr, hinges=hinges, pf=pf, pm=pm, q=q, E=E, I=I)


def gen_f(rng, r0, id='f'):
    c = gen_s(rng, r0, id); L = c['L']; EI = c['E'] * c['I']
    items = [a for a, _ in c['sup']] + [s[0] for s in c['springs']] + c['hinges'] + [a for a, _ in c['pf']]
    near = lambda: min(L * (1 - 1e-12), max(L * 1e-12, rng.choice(items) + rng.choice([-1, 1]) * r0 * L)) if items and rng.random() < .5 else rng.uniform(0, L)
    ei = sorted({near(): EI * 10 ** rng.uniform(-2, 2) for _ in range(rng.randint(0, 3))}.items())
    ei = [[x, v] for x, v in ei if 0 < x < L]
    mag = max(abs(p[1]) for p in c['pf']) / L if c['pf'] else 1e3
    lin = []
    for _ in range(rng.randint(0, 2)):
        a = near(); b = a + (r0 * L if rng.random() < .4 else rng.uniform(0, L - a))
        if b > L or b <= a: continue
        lin.append([a, b, mag * rng.uniform(-2, 2), mag * rng.uniform(-2, 2)])
    for _ in range(rng.randint(0, 2) if rng.random() < .5 else 0):   # curvature draws, discarded
        a = rng.uniform(0, L); b = rng.uniform(a, L)
        if b > a: rng.uniform(-5, -2); rng.choice([-1, 1])
    return case(id, L, [tuple(s) for s in c['sup']], springs=[tuple(s) for s in c['springs']], hinges=c['hinges'],
                pf=[tuple(p) for p in c['pf']], pm=[tuple(p) for p in c['pm']], q=[tuple(p) for p in c['q']],
                E=c['E'], I=c['I'], ei=ei, lin=lin)
