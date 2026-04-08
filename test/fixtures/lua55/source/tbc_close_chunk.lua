local log = ""

local mt = {
  __close = function(self)
    log = log .. self.tag
  end
}

local function run()
  local a <close> = setmetatable({ tag = "a" }, mt)

  do
    local x <close> = setmetatable({ tag = "x" }, mt)
  end

  local b <close> = setmetatable({ tag = "b" }, mt)
  return 42
end

local result = run()
return result, log
