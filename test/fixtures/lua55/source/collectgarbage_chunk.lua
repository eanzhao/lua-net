local before = collectgarbage("isrunning")
local stop = collectgarbage("stop")
local stopped = collectgarbage("isrunning")
local count_positive = collectgarbage("count") > 0
local step = collectgarbage("step")
local collected = collectgarbage("collect")
local restart = collectgarbage("restart")
local after = collectgarbage("isrunning")

return before, stop, stopped, count_positive, step, collected, restart, after
