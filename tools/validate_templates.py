"""Offline OpenCV-only regression check; does not simulate Windows OCR or inputs."""
import json
import pathlib
import sys
import cv2
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parents[1]
CONFIG = ROOT / 'src/AbyssAuto'
EXPECTED = {'abyss': 'menu', 'husang': 'destinations', 'kwanggi': 'destinations',
            'moolgil': 'destinations', 'enter': 'enter', 'result': 'result'}


def main():
    targets = json.loads((CONFIG / 'targets.json').read_text())
    frames = {p.stem: cv2.imread(str(p)) for p in sorted((ROOT / 'validation/screens').glob('*.png'))}
    records = []
    failures = []
    for name, positive in EXPECTED.items():
        target = targets[name]
        r = target['Region']
        for screen, frame in frames.items():
            area = frame[r['Top']:r['Bottom'], r['Left']:r['Right']]
            best = {'score': -1., 'image': None, 'bounds': None}
            for image in target['Images']:
                path = CONFIG / 'images' / image
                if not path.exists():
                    continue
                template = cv2.imread(str(path))
                h, w = template.shape[:2]
                assert h >= 12 and w >= 12
                assert h <= area.shape[0] and w <= area.shape[1]
                assert np.std(template, axis=(0, 1)).sum() >= 15
                _, score, _, point = cv2.minMaxLoc(cv2.matchTemplate(area, template, cv2.TM_CCOEFF_NORMED))
                if score > best['score']:
                    best = dict(score=float(score), image=image,
                                bounds=[point[0]+r['Left'], point[1]+r['Top'], w, h])
            passed = best['score'] >= target['Threshold']
            expected = screen == positive
            if passed != expected:
                failures.append(f'{name}/{screen}: expected {expected}, score {best["score"]:.6f}')
            records.append(dict(target=name, screen=screen, **best, threshold=target['Threshold'],
                                imagePassed=passed, expectedImageMatch=expected))
    # Measure background color in manually identified text boxes; these are NOT OCR detections.
    samples = [dict(screen='enter', target='enter', bounds=[947, 800, 91, 28]),
               dict(screen='reward', target='retry', bounds=[602, 800, 94, 28])]
    colors = []
    for sample in samples:
        r = targets[sample['target']]['Region']
        x, y, w, h = sample['bounds']
        left, top = max(r['Left'], x-int(w*.4)), max(r['Top'], y-int(h*.3))
        right, bottom = min(r['Right'], left+int(w*1.8)), min(r['Bottom'], top+int(h*1.6))
        b, g, red = np.median(frames[sample['screen']][top:bottom, left:right], axis=(0, 1))
        colors.append(dict(**sample, bgrMedian=[float(b), float(g), float(red)],
                           greenDominance=float(g-max(red,b)), blueDominance=float(b-max(red,g)),
                           note='manual text box, not Windows OCR output'))
    report = dict(opencv=cv2.__version__, imageChecks=len(records), failures=failures,
                  records=records, colorMeasurements=colors,
                  limitation='One supplied positive per target is also the template source; no independent positive, Windows OCR, live input or temporal validation.')
    output = ROOT / 'validation/template-report.json'
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2))
    print(f'OpenCV {cv2.__version__}: {len(records)} image comparisons, {len(failures)} failures')
    for name in EXPECTED:
        rows = [r for r in records if r['target'] == name]
        pos = next(r['score'] for r in rows if r['expectedImageMatch'])
        neg = max(r['score'] for r in rows if not r['expectedImageMatch'])
        print(f'{name}: source positive {pos:.6f}, max other-screen {neg:.6f}, threshold {targets[name]["Threshold"]}')
    for row in colors:
        print(f'{row["target"]} manual background: green {row["greenDominance"]:.1f}, blue {row["blueDominance"]:.1f}')
    for failure in failures:
        print(failure, file=sys.stderr)
    return int(bool(failures))


if __name__ == '__main__':
    raise SystemExit(main())
