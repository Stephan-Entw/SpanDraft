# Scratch: internal hinges on the support-span path, against exact fractions.
import random, sys
from fractions import Fraction as Fr
import numpy as np
sys.path.insert(0, '.')
import fuzz, limit, spanproto
from fuzz import mac, H


class Mechanism(Exception):
    pass


class ExactH(fuzz.Exact):
    """Exact rational reference with rotation jumps D at hinges and M(h) = 0."""
    def __init__(s, c):
        F = lambda v: Fr(float(v))
        s.L = F(c['L'])
        s.EI = F(c['E']) * F(c['I'])
        s.sup = [(F(a), t) for a, t in c['sup']]
        s.pf = [(F(a), F(v)) for a, v in c['pf']]
        s.pm = [(F(a), F(v)) for a, v in c['pm']]
        s.q = [(F(a), F(b), F(v)) for a, b, v in c['q']]
        s.h = [F(x) for x in c.get('hinges', [])]
        fixed = [a for a, t in s.sup if t == 'Fixed']
        ns, nf, nh = len(s.sup), len(fixed), len(s.h)
        n = ns + nf + 2 + nh
        A = [[Fr(0)] * (n + 1) for _ in range(n)]
        r = 0
        for i, (a, _) in enumerate(s.sup):
            A[r][i] = Fr(1)
        A[r][n] = -(sum(v for _, v in s.pf) + sum(v * (b - a) for a, b, v in s.q))
        r += 1
        for i, (a, _) in enumerate(s.sup):
            A[r][i] = a
        for j in range(nf):
            A[r][ns + j] = Fr(1)
        A[r][n] = -(sum(v * a for a, v in s.pf) + sum(v for _, v in s.pm) + sum(v * (b - a) * (a + b) / 2 for a, b, v in s.q))
        r += 1
        for x, _ in s.sup:
            for i, (a, _) in enumerate(s.sup):
                A[r][i] = mac(x, a, 3) / 6
            for j, a in enumerate(fixed):
                A[r][ns + j] = -mac(x, a, 2) / 2
            A[r][ns + nf] = x
            A[r][ns + nf + 1] = Fr(1)
            for k, h in enumerate(s.h):
                A[r][ns + nf + 2 + k] = mac(x, h, 1)
            A[r][n] = -s.wload(x)
            r += 1
        for x in fixed:
            for i, (a, _) in enumerate(s.sup):
                A[r][i] = mac(x, a, 2) / 2
            for j, a in enumerate(fixed):
                A[r][ns + j] = -mac(x, a, 1)
            A[r][ns + nf] = Fr(1)
            for k, h in enumerate(s.h):
                A[r][ns + nf + 2 + k] = Fr(1) if x > h else Fr(0)
            A[r][n] = -s.tload(x)
            r += 1
        for h in s.h:
            for i, (a, _) in enumerate(s.sup):
                A[r][i] = mac(h, a, 1)
            for j, a in enumerate(fixed):
                A[r][ns + j] = -(Fr(1) if h > a else Fr(0))
            A[r][n] = -(sum(v * mac(h, a, 1) for a, v in s.pf) - sum(v for a, v in s.pm if h > a)
                        + sum(v * (mac(h, a, 2) - mac(h, b, 2)) / 2 for a, b, v in s.q))
            r += 1
        for i in range(n):
            p = next((k for k in range(i, n) if A[k][i] != 0), None)
            if p is None:
                raise Mechanism()
            A[i], A[p] = A[p], A[i]
            d = A[i][i]
            for k in range(n):
                if k != i and A[k][i] != 0:
                    f = A[k][i] / d
                    A[k] = [u - f * v for u, v in zip(A[k], A[i])]
        sol = [A[i][n] / A[i][i] for i in range(n)]
        s.Ry = sol[:ns]
        s.Rm = sol[ns:ns + nf]
        s.C1 = sol[ns + nf]
        s.C2 = sol[ns + nf + 1]
        s.D = sol[ns + nf + 2:]
        s.fixed = fixed
        s.F = [(a, v) for a, v in s.pf] + [(a, r_) for (a, _), r_ in zip(s.sup, s.Ry)]
        s.C = [(a, v) for a, v in s.pm] + [(a, r_) for a, r_ in zip(fixed, s.Rm)]

    def w(s, x):
        return fuzz.Exact.w(s, x) + sum(d * mac(x, h, 1) for d, h in zip(s.D, s.h)) / s.EI

    def t(s, x, side='R'):
        return fuzz.Exact.t(s, x) + sum(d * H(x, h, side) for d, h in zip(s.D, s.h)) / s.EI

    def events(s):
        return sorted(set(fuzz.Exact.events(s)) | set(s.h))

    def extrema(s):
        ev = s.events()
        wv, Vv, Mv = [], [], []
        for a, b in zip(ev, ev[1:]):
            wv += [s.w(a), s.w(b)]
            Vv += [s.V(a, 'R'), s.V(b, 'L')]
            Mv += [s.M(a, 'R'), s.M(b, 'L')]
            va, vb = s.V(a, 'R'), s.V(b, 'L')
            if va * vb < 0:
                Mv.append(s.M(a + (b - a) * va / (va - vb), 'R'))
            xs = [a + (b - a) * Fr(i, 5) for i in (1, 2, 3, 4)]
            ys = [s.t(x) for x in xs]
            m = max(abs(y) for y in ys)
            if m == 0:
                continue
            co = np.polyfit([.2, .4, .6, .8], [float(y / m) for y in ys], 3)
            for r in np.roots(co):
                if abs(r.imag) < 1e-6 and 0 < r.real < 1:
                    wv.append(s.w(a + (b - a) * Fr(float(r.real))))
        return dict(wMin=min(wv), wMax=max(wv), VMin=min(Vv), VMax=max(Vv), MMin=min(Mv), MMax=max(Mv))


def stable(c):
    """Kinematic count along the chain; depends on the order of items only."""
    L = c['L']
    sup = sorted((a, t) for a, t in c['sup'])
    hinges = sorted(c.get('hinges', []))
    items = sorted([(a, 0, t) for a, t in sup] + [(h, 1, 'H') for h in hinges], key=lambda z: (z[0], z[1]))
    f = 2
    for x, kind, t in items:
        if kind == 0:
            f = max(f - (2 if t == 'Fixed' else 1), 0)
        else:
            if f == 2:
                return False
            at_support = any(a == x for a, _ in sup)
            if at_support:
                if f != 0:
                    return False
                f = 1
            else:
                f = 1 if f == 0 else 2
    return f == 0


class Seg(spanproto.Seg):
    pass


class HingeSolver:
    """Equations at supports and hinges; hinge rotations are condensed out of each span."""
    def __init__(s, c):
        s.c = c
        s.L = c['L']
        s.EI = c['E'] * c['I']
        if not stable(c):
            raise Mechanism()
        sup = {a: t for a, t in c['sup']}
        s.hset = set(c.get('hinges', []))
        s.nodes = sorted(set(sup) | s.hset)
        n = len(s.nodes)
        s.sup = sup
        s.spans = [spanproto.Seg(c, s.nodes[i], s.nodes[i + 1]) for i in range(n - 1)]
        for sp, b in zip(s.spans, s.nodes[1:]):
            # Loads exactly on the right node belong to the node.
            sp.pf = [(x, v) for x, v in sp.pf if x < sp.l]
            sp.pm = [(x, v) for x, v in sp.pm if x < sp.l]
        first, last = s.nodes[0], s.nodes[-1]
        s.over_r = spanproto.Seg(c, last, s.L) if last < s.L else None
        s.over_l = None
        if first > 0:
            o = spanproto.Seg(dict(pf=[], pm=[], q=[]), 0.0, first)
            o.pf = [(x, v) for x, v in c['pf'] if x < first]
            o.pm = [(x, v) for x, v in c['pm'] if x < first]
            o.q = [(p, min(r, first), v) for p, r, v in c['q'] if p < first]
            s.over_l = o
        s.Fnode = [sum(v for x, v in c['pf'] if x == a) for a in s.nodes]
        s.Cnode = [sum(v for x, v in c['pm'] if x == a) for a in s.nodes]
        s.ML0 = s.over_l.Ml(first, 'R') if s.over_l else 0.0
        s.VL0 = s.over_l.Vl(first, 'R') if s.over_l else 0.0
        if s.over_r:
            o = s.over_r
            s.VRn = -o.Vl(o.l, 'R')
            s.MRn = -s.VRn * o.l - o.Ml(o.l, 'R')
        else:
            s.VRn = s.MRn = 0.0
        # Unknowns: w at unsupported nodes, theta at nodes without hinge or clamp.
        s.dof = {}
        for i, a in enumerate(s.nodes):
            if a not in sup:
                s.dof[('w', i)] = len(s.dof)
            if a not in s.hset and sup.get(a) != 'Fixed':
                s.dof[('t', i)] = len(s.dof)
        nd = len(s.dof)
        s.u = np.zeros(nd)
        K = np.zeros((nd, nd))
        b0 = s.residual(np.zeros(nd))
        for j in range(nd):
            e = np.zeros(nd)
            e[j] = 1.0
            K[:, j] = s.stiff_residual(e)
        if nd:
            d = np.array([1 / np.sqrt(abs(K[i, i])) for i in range(nd)])
            A = d[:, None] * K * d
            s.cond = np.linalg.cond(A)
            y = np.linalg.solve(A, -b0 * d)
            s.u = y * d
        else:
            s.cond = 1.0

    def nodal(s, u, i):
        w = u[s.dof[('w', i)]] if ('w', i) in s.dof else 0.0
        t = u[s.dof[('t', i)]] if ('t', i) in s.dof else 0.0
        return w, t

    def span_ends(s, i, u, with_loads=True):
        sp = s.spans[i]
        l = sp.l
        wa, ta = s.nodal(u, i)
        wb, tb = s.nodal(u, i + 1)
        rl, rr = s.nodes[i] in s.hset, s.nodes[i + 1] in s.hset
        P, T, Ml, Vl = (sp.P(l), sp.T(l), sp.Ml(l, 'R'), sp.Vl(l, 'R')) if with_loads else (0., 0., 0., 0.)
        EI, dw = s.EI, wb - wa
        if not rl and not rr:
            Va = 6 * EI * (ta + tb) / l ** 2 - 12 * EI * dw / l ** 3 + (12 * P / l - 6 * T) / l ** 2
            Ma = -EI * (4 * ta + 2 * tb) / l + 6 * EI * dw / l ** 2 + 2 * T / l - 6 * P / l ** 2
        elif rl and not rr:
            Va = 3 * (EI * tb * l - T * l + P - EI * dw) / l ** 3
            Ma = 0.0
        elif rr and not rl:
            Va = 3 * (EI * ta * l - Ml * l ** 2 / 2 + P - EI * dw) / l ** 3
            Ma = -Va * l - Ml
        else:
            Va = -Ml / l
            Ma = 0.0
        return Va, Ma, Va + Vl, Ma + Va * l + Ml, (P, T)

    def balance(s, u, with_loads):
        out = np.zeros(len(s.dof))
        n = len(s.nodes)
        for i in range(n):
            if i < n - 1:
                VR, MR, *_ = s.span_ends(i, u, with_loads)
            else:
                VR, MR = (s.VRn, s.MRn) if with_loads else (0., 0.)
            if i > 0:
                _, _, VL, ML, _ = s.span_ends(i - 1, u, with_loads)
            else:
                VL, ML = (s.VL0, s.ML0) if with_loads else (0., 0.)
            if ('w', i) in s.dof:
                out[s.dof[('w', i)]] = -(VR - VL - (s.Fnode[i] if with_loads else 0.))
            if ('t', i) in s.dof:
                out[s.dof[('t', i)]] = -(MR - ML + (s.Cnode[i] if with_loads else 0.))
        return out

    def residual(s, u):
        return s.balance(u, True)

    def stiff_residual(s, e):
        return s.balance(e, False)

    def at(s, x, side):
        n = s.nodes
        if x < n[0] or (x == n[0] and side == 'L' and n[0] > 0):
            o = s.over_l
            _, t0 = s.nodal(s.u, 0)
            if n[0] in s.hset:
                raise Mechanism()
            C1 = s.EI * t0 - o.T(o.l)
            C2 = -C1 * o.l - o.P(o.l)
            return [(o.P(x) + C1 * x + C2) / s.EI, (o.T(x) + C1) / s.EI, o.Vl(x, side), o.Ml(x, side)]
        i = 0
        for j, a in enumerate(n):
            if x > a or (x == a and side == 'R'):
                i = j
        seg = s.spans[i] if i < len(s.spans) else s.over_r
        if seg is None:
            i -= 1
            seg = s.spans[i]
        if i < len(s.spans):
            Va, Ma, *_ = s.span_ends(i, s.u)
        else:
            Va, Ma = s.VRn, s.MRn
        wa, ta = s.nodal(s.u, i)
        rl = n[i] in s.hset
        rr = i < len(s.spans) and n[i + 1] in s.hset
        if rl:  # rotation on the released side from the far end of the span
            sp = s.spans[i]
            l = sp.l
            wb, tb = s.nodal(s.u, i + 1)
            ta = ((wb - wa) * s.EI - Va * l ** 3 / 6 - Ma * l ** 2 / 2 - sp.P(l)) / l / s.EI
        t = x - n[i]
        return [wa + ta * t + (Va * t ** 3 / 6 + Ma * t ** 2 / 2 + seg.P(t)) / s.EI,
                ta + (Va * t ** 2 / 2 + Ma * t + seg.T(t)) / s.EI,
                Va + seg.Vl(t, side), Ma + Va * t + seg.Ml(t, side)]


def pts(c):
    L = c['L']
    ev = sorted({0.0, L} | {s[0] for s in c['sup']} | {a for a, _ in c['pf']} | {a for a, _ in c['pm']}
                | {x for a, b, _ in c['q'] for x in (a, b)} | set(c.get('hinges', [])))
    out = []
    for j, x in enumerate(ev):
        if j > 0:
            out.append([x, 'L'])
        if j < len(ev) - 1:
            out.append([x, 'R'])
            for f in (.3, .7):
                out.append([x + (ev[j + 1] - x) * f, 'R'])
    return out


def case(id, L, sup, hinges=(), pf=(), pm=(), q=(), E=210e9, I=None):
    I = I if I is not None else 8e-6 * L ** 4 / 4 ** 4
    c = dict(id=id, kind='h', L=L, E=E, A=.003 * L * L / 16, I=I, sup=[[a, t] for a, t in sup], hinges=list(hinges),
             pf=[list(p) for p in pf], pm=[list(p) for p in pm], q=[list(p) for p in q])
    c['pts'] = pts(c)
    return c


def err(c):
    e = ExactH(c)
    so = HingeSolver(c)
    rows = []
    for x, sd in c['pts']:
        X = Fr(float(x))
        rows.append(([float(e.w(X)), float(e.t(X, sd)), float(e.V(X, sd)), float(e.M(X, sd))], so.at(float(x), sd)))
    sc = [max(abs(r[0][i]) for r in rows) or 1. for i in range(4)]
    v, mm = sc[2], sc[3]
    sc[2] = max(v, mm / c['L'])
    sc[3] = max(mm, v * c['L'])
    EI = c['E'] * c['I']
    sc[1] = max(sc[1], sc[3] * c['L'] / EI)
    sc[0] = max(sc[0], sc[3] * c['L'] ** 2 / EI)
    return max(max(abs(r[0][i] - r[1][i]) / sc[i] for r in rows) for i in range(4)), so.cond


def sweep():
    L = 10.0
    P = -5000.
    rs = [1e-1, 1e-2, 1e-3, 1e-4, 1e-5, 1e-6, 1e-7, 1e-8, 1e-9]
    T = {
        'Gerber: Gelenk nahe Mittellager': lambda g: dict(sup=[(0., 'Pinned'), (.5 * L, 'Roller'), (L, 'Roller')], hinges=[.5 * L + g], pf=[(.3 * L, P), (.8 * L, P)]),
        'Einspannung, Gelenk nahe daran': lambda g: dict(sup=[(0., 'Fixed'), (L, 'Roller')], hinges=[g], pf=[(.5 * L, P)]),
        'Einspannung+Rolle, Gelenk nahe Rolle': lambda g: dict(sup=[(0., 'Fixed'), (L, 'Roller')], hinges=[L - g], pf=[(.5 * L, P)]),
        'Beidseitig fest, Gelenk Mitte, Last nah': lambda g: dict(sup=[(0., 'Fixed'), (L, 'Fixed')], hinges=[.5 * L], pf=[(.5 * L + g, P)]),
        'Einhaengetraeger, Gelenke dicht': lambda g: dict(sup=[(0., 'Fixed'), (L, 'Fixed')], hinges=[.5 * L, .5 * L + g], pf=[(.25 * L, P)]),
        'Einhaengetraeger normal, Last nah Gelenk': lambda g: dict(sup=[(0., 'Fixed'), (L, 'Fixed')], hinges=[.3 * L, .7 * L], pf=[(.3 * L + g, P)]),
        'Ueberhang-Gerber: Gelenk nahe Lager': lambda g: dict(sup=[(0., 'Pinned'), (.4 * L, 'Roller'), (L, 'Roller')], hinges=[.4 * L + g], pf=[(.7 * L, P)], q=[(0., L, -2000.)]),
        'Zwei Lager dicht plus Gelenk': lambda g: dict(sup=[(0., 'Pinned'), (.3 * L, 'Roller'), (.3 * L + g, 'Roller'), (L, 'Roller')], hinges=[.6 * L], pf=[(.5 * L, P)]),
        'Gelenk genau auf Lager': lambda g: dict(sup=[(0., 'Pinned'), (.5 * L, 'Roller'), (L, 'Roller')], hinges=[.5 * L], pf=[(.25 * L, P), (.5 * L + g, P)]),
    }
    print('%-42s' % 'Abstand/L' + ''.join('%9.0e' % r for r in rs))
    worst_cond = {}
    for name, f in T.items():
        row = ''
        for r in rs:
            c = case(f'{name}|{r}', L, **f(r * L))
            try:
                e, cond = err(c)
                row += '%9.0e' % e
                worst_cond[name] = max(worst_cond.get(name, 0), cond)
            except Mechanism:
                row += '     MECH'
            except np.linalg.LinAlgError:
                row += '     SING'
        print('%-42s' % name + row, 'cond %.0e' % worst_cond.get(name, 0), flush=True)


def rand_stability(N):
    rng = random.Random(9)
    agree = wrongstable = wrongunstable = 0
    tally = [0, 0]
    for i in range(N):
        L = 10.0
        slots = sorted(rng.sample([round(k * .5, 2) for k in range(0, 21)], rng.randint(1, 7)))
        sup, hinges = [], []
        for x in slots:
            k = rng.random()
            if k < .35:
                sup.append((x, 'Roller'))
            elif k < .55:
                sup.append((x, 'Pinned'))
            elif k < .7:
                sup.append((x, 'Fixed'))
            elif 0 < x < L:
                hinges.append(x)
        if rng.random() < .15 and hinges and sup:
            hx = rng.choice([a for a, t in sup if t != 'Fixed' and 0 < a < L] or [None])
            if hx is not None and hx not in hinges:
                hinges.append(hx)
        if not sup:
            continue
        c = case(f's{i}', L, sup, hinges=hinges, pf=[(4.3, -1000.)])
        try:
            ExactH(c)
            exact_stable = True
        except Mechanism:
            exact_stable = False
        ours = stable(c)
        tally[exact_stable] += 1
        if ours == exact_stable:
            agree += 1
        elif ours:
            wrongstable += 1
            print('  faelschlich stabil', sup, hinges)
        else:
            wrongunstable += 1
            print('  faelschlich instabil', sup, hinges)
    print('Stabilitaet: stimmt %d, faelschlich stabil %d, faelschlich instabil %d | exakt instabil %d, stabil %d' % (agree, wrongstable, wrongunstable, tally[0], tally[1]))


if __name__ == '__main__':
    if 'sweep' in sys.argv:
        sweep()
    if 'stab' in sys.argv:
        rand_stability(3000)
