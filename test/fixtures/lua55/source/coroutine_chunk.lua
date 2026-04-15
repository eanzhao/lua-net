local main_thread, is_main = coroutine.running()
local main_yieldable = coroutine.isyieldable()

local co = coroutine.create(function(a)
  local self_thread, self_is_main = coroutine.running()
  local self_status = coroutine.status(self_thread)
  local main_status = coroutine.status(main_thread)
  local self_yieldable = coroutine.isyieldable()
  local r1, r2 = coroutine.yield(a + 1, self_status, main_status, self_is_main and 1 or 0, self_yieldable and 1 or 0)
  return r1, r2, coroutine.status(self_thread)
end)

local co_status0 = coroutine.status(co)
local ok1, y1, y2, y3, y4, y5 = coroutine.resume(co, 40)
local co_status1 = coroutine.status(co)
local ok2, r1, r2, r3 = coroutine.resume(co, "x", "y")
local co_status2 = coroutine.status(co)

local wrapped = coroutine.wrap(function()
  local value = coroutine.yield("wrap-yield")
  return "wrap-" .. value
end)

local wrap1 = wrapped()
local wrap2 = wrapped("done")

local closed = 0
local closer = coroutine.create(function()
  local guard <close> = setmetatable({}, {
    __close = function()
      closed = closed + 1
    end
  })

  return coroutine.yield("pause")
end)

local closer_ok, closer_value = coroutine.resume(closer)
local closer_status1 = coroutine.status(closer)
local close_ok = coroutine.close(closer)
local closer_status2 = coroutine.status(closer)

local dead = coroutine.create(function()
  return 7
end)

local dead_ok, dead_value = coroutine.resume(dead)
local dead_close_ok = coroutine.close(dead)

local err = coroutine.create(function()
  error("co-boom")
end)

local err_ok, err_value = coroutine.resume(err)
local err_close_ok, err_close_value = coroutine.close(err)

local self_close = coroutine.create(function()
  coroutine.close()
  return 99
end)

local self_close_ok = coroutine.resume(self_close)
local self_close_status = coroutine.status(self_close)

local thread_type = type(main_thread)

return
  thread_type,
  is_main and 1 or 0,
  main_yieldable and 1 or 0,
  co_status0,
  ok1 and 1 or 0,
  y1,
  y2,
  y3,
  y4,
  y5,
  co_status1,
  ok2 and 1 or 0,
  r1,
  r2,
  r3,
  co_status2,
  wrap1,
  wrap2,
  closer_ok and 1 or 0,
  closer_value,
  closer_status1,
  close_ok and 1 or 0,
  closer_status2,
  closed,
  dead_ok and 1 or 0,
  dead_value,
  dead_close_ok and 1 or 0,
  err_ok and 1 or 0,
  err_value,
  err_close_ok and 1 or 0,
  err_close_value,
  self_close_ok and 1 or 0,
  self_close_status
