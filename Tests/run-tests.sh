#!/bin/bash
dotnet tool restore

# give everyone write permissions
# without doing this I recieved the following error from jb
#
#   error: Files still read-only: /srv/Tests/HelloWorld/Program.cs
sudo chmod -R 777 .

git --version;

sudo git diff --name-only bb3f9ba222^ bb3f9ba222

failed=0
passed=0

for d in */ ; do

    test_script="$d/test.sh"

    if test -f "$test_script"; then

        echo "running test $d"

        "$d/test.sh" "net6.0"

        if [ $? -ne 0 ]; then
            echo "$d failed"
            failed=$(($failed + 1))
        else
            echo "$d passed"
            passed=$(($passed + 1))
        fi

        git restore HelloWorld/
    fi
done

echo "passed: $passed failed: $failed"

if (($failed > 0)); then
    exit 1
fi
