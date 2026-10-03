#!/bin/bash
dotnet tool restore

failed=0
passed=0

for d in */ ; do

    test_script="${d}test.sh"

    if test -f "$test_script"; then

        chmod +x "$test_script" 2>/dev/null || true

        echo "running test $d"

        "$test_script" "net10.0"

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
