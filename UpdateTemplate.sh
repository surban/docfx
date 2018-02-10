#!/bin/bash
set -e

# Check if node exists globally
if ! which node > /dev/null; then 
    echo "ERROR: UpdateTemplate.sh requires node installed globally."
    exit 1
fi

MYDIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
pushd "$MYDIR"

TemplateHome="${MYDIR}/src/docfx.website.themes/"
DefaultTemplate="${TemplateHome}default/"
GulpCommand="${DefaultTemplate}node_modules/gulp/bin/gulp"

cd "$DefaultTemplate"
npm install
node ./node_modules/bower/bin/bower install
node "$GulpCommand"

cd "$TemplateHome"
npm install
node "$GulpCommand"

popd
