"""Google Play "Data safety" answers for Potion Pop!, as the CSV the Play Console imports/exports.

Usage: python3 Tools/release/data_safety.py <template.csv> [Docs/loja/seguranca-dos-dados.csv]
The template is any CSV exported from Play Console > App content > Data safety (all values are rewritten); the
Car Racing one works too: ~/Desktop/games/car-racing/docs/loja/seguranca-dos-dados.csv.

What leaves the device (same approach as Car Racing; keep this, the privacy policy at
brunogames.com.br/jogos/potion-pop/privacidade and the App Store "App Privacy" answers in
Docs/loja/privacidade-apple.json in sync):
* Google Mobile Ads SDK (AdMob, not child-directed, AD_ID permission kept): approximate location (from the IP),
  device or other IDs (advertising ID, App Set ID), app interactions, crash logs and diagnostics. Required (every
  player sees ads), collected AND shared (Google uses them for its own purposes under its policies).
* Optional account (Sign in with Google on Android; Apple too on iOS) -> Firebase Authentication + Cloud Firestore,
  Google acting as our processor (so "collected", not "shared"): user ID, email address and name from the Google
  account, the player name (a nickname is "Name" in Play's definitions) and the progress/save, avatar, stars and
  level ("App activity > Other actions": gameplay). Only collected when the player signs in, so optional.
No analytics SDK, no Firebase SDK (Auth and Firestore are called over REST), no purchases, no chat.
"""
import csv
import sys

DELETION_URL = 'https://brunogames.com.br/jogos/potion-pop/privacidade#excluir'

GENERAL = {
    'PSL_DATA_COLLECTION_COLLECTS_PERSONAL_DATA': 'true',
    'PSL_DATA_COLLECTION_ENCRYPTED_IN_TRANSIT': 'true',           # HTTPS only (AdMob, Firebase REST)
    ('PSL_SUPPORTED_ACCOUNT_CREATION_METHODS', 'PSL_ACM_OAUTH'): 'true',   # Sign in with Google / Apple
    'PSL_ACCOUNT_DELETION_URL': DELETION_URL,
    ('PSL_SUPPORT_DATA_DELETION_BY_USER', 'DATA_DELETION_YES'): 'true',  # in the game and by email
    'PSL_DATA_DELETION_URL': DELETION_URL,
    # PSL_HAS_OUTSIDE_APP_ACCOUNTS stays blank: Play refuses an answer to it when the account is created in the app.
}

ADS, ANALYTICS, FRAUD = 'PSL_ADVERTISING', 'PSL_ANALYTICS', 'PSL_FRAUD_PREVENTION_SECURITY'
FUNCTIONALITY, ACCOUNT = 'PSL_APP_FUNCTIONALITY', 'PSL_ACCOUNT_MANAGEMENT'
REQUIRED, OPTIONAL = 'PSL_DATA_USAGE_USER_CONTROL_REQUIRED', 'PSL_DATA_USAGE_USER_CONTROL_OPTIONAL'

# data type: (category, collection purposes, sharing purposes (empty = not shared), user control). None is ephemeral.
TYPES = {
    # AdMob (Google Mobile Ads SDK)
    'PSL_APPROX_LOCATION': ('PSL_DATA_TYPES_LOCATION', [ADS, ANALYTICS, FRAUD], [ADS, ANALYTICS, FRAUD], REQUIRED),
    'PSL_USER_INTERACTION': ('PSL_DATA_TYPES_APP_ACTIVITY', [ADS, ANALYTICS], [ADS, ANALYTICS], REQUIRED),
    'PSL_CRASH_LOGS': ('PSL_DATA_TYPES_APP_PERFORMANCE', [ANALYTICS], [ANALYTICS], REQUIRED),
    'PSL_PERFORMANCE_DIAGNOSTICS': ('PSL_DATA_TYPES_APP_PERFORMANCE', [ANALYTICS], [ANALYTICS], REQUIRED),
    'PSL_DEVICE_ID': ('PSL_DATA_TYPES_IDENTIFIERS', [ADS, ANALYTICS, FRAUD], [ADS, ANALYTICS, FRAUD], REQUIRED),
    # Optional account (Firebase Authentication + Cloud Firestore, processor)
    'PSL_USER_ACCOUNT': ('PSL_DATA_TYPES_PERSONAL', [FUNCTIONALITY, ACCOUNT], [], OPTIONAL),
    'PSL_EMAIL': ('PSL_DATA_TYPES_PERSONAL', [FUNCTIONALITY, ACCOUNT], [], OPTIONAL),
    'PSL_NAME': ('PSL_DATA_TYPES_PERSONAL', [FUNCTIONALITY, ACCOUNT], [], OPTIONAL),
    'PSL_OTHER_APP_ACTIVITY': ('PSL_DATA_TYPES_APP_ACTIVITY', [FUNCTIONALITY], [], OPTIONAL),
}


def answer(q, r):
    if (q, r) in GENERAL:
        return GENERAL[(q, r)]
    if not r and q in GENERAL:
        return GENERAL[q]
    for kind, (category, collected, shared, control) in TYPES.items():
        if q == category and r == kind:
            return 'true'
        prefix = f'PSL_DATA_USAGE_RESPONSES:{kind}:'
        if not q.startswith(prefix):
            continue
        sub = q[len(prefix):]
        if sub == 'PSL_DATA_USAGE_COLLECTION_AND_SHARING':
            if r == 'PSL_DATA_USAGE_ONLY_COLLECTED' or (r == 'PSL_DATA_USAGE_ONLY_SHARED' and shared):
                return 'true'
        if sub == 'PSL_DATA_USAGE_EPHEMERAL':
            return 'false'
        if sub == 'DATA_USAGE_USER_CONTROL' and r == control:
            return 'true'
        if sub == 'DATA_USAGE_COLLECTION_PURPOSE' and r in collected:
            return 'true'
        if sub == 'DATA_USAGE_SHARING_PURPOSE' and r in shared:
            return 'true'
    return ''


def main():
    rows = list(csv.reader(open(sys.argv[1], encoding='utf-8')))
    out = sys.argv[2] if len(sys.argv) > 2 else 'Docs/loja/seguranca-dos-dados.csv'
    used, asked = set(), {row[0] for row in rows[1:]}
    for row in rows[1:]:
        row[2] = answer(row[0], row[1])
        if row[2] and row[0].startswith('PSL_DATA_USAGE_RESPONSES:'):
            used.add(row[0].split(':')[1])
    missing = [t for t in TYPES if t not in used] + [q if isinstance(q, str) else q[0] for q in GENERAL if (q if isinstance(q, str) else q[0]) not in asked]
    if missing:
        sys.exit(f'questions or data types missing from the template (did Google change the form?): {missing}')
    with open(out, 'w', encoding='utf-8', newline='') as f:
        csv.writer(f).writerows(rows)
    print(f'{out}: {sum(1 for r in rows[1:] if r[2])} answers')


if __name__ == '__main__':
    main()
