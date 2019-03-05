﻿using AutoMapper;
using KM.Common;
using KMHC.Infrastructure;
using KMHC.Infrastructure.Security;
using KMHC.SLTC.Business.Entity;
using KMHC.SLTC.Business.Entity.Base;
using KMHC.SLTC.Business.Entity.Filter;
using KMHC.SLTC.Business.Entity.Model;
using KMHC.SLTC.Business.Interface;
using KMHC.SLTC.Persistence;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KMHC.SLTC.Business.Implement
{
    public class OrganizationManageService : BaseService, IOrganizationManageService
    {
        #region 機構
        public BaseResponse<IList<Organization>> QueryOrg(BaseRequest<OrganizationFilter> request)
        {
            var response = base.Query<LTC_ORG, Organization>(request, (q) =>
            {
                if (request != null && !string.IsNullOrEmpty(request.Data.OrgName))
                {
                    q = q.Where(m => m.ORGNAME.Contains(request.Data.OrgName));
                }
                q = q.OrderBy(m => m.ORGID);
                return q;
            });
            return response;
        }
        /// <summary>
        /// 根据ordId 获得当前系统 Admin权限菜单
        /// </summary>
        /// <param name="orgID"></param>
        /// <returns></returns>
        public BaseResponse<Organization> GetOrg(string orgID)
        {
            var response = base.Get<LTC_ORG, Organization>((q) => q.ORGID == orgID);
            string roleType = EnumRoleType.Admin.ToString();
            var role = unitOfWork.GetRepository<LTC_ROLES>().dbSet.Where(m => m.ORGID == orgID && m.ROLETYPE == roleType && m.SYSTYPE == SecurityHelper.CurrentPrincipal.CurrentLoginSys).FirstOrDefault();
            if (role != null)
            {
                response.Data.RoleId = role.ROLEID;
            }
            return response;
        }

        public BaseResponse<Organization> SaveOrg(Organization request)
        {
            if (string.IsNullOrEmpty(request.OrgId))
            {
                request.OrgId = base.GenerateCode("ProduceOrg", EnumCodeKey.OrgId);
                //request.OrgId = base.GenerateCode(request.GroupId, EnumCodeKey.OrgId);
                //创建org 同时 创建该机构 RoleID编码规则
                request.RoleId = base.GenerateCode(EnumCodeKey.RoleId.ToString(), EnumCodeKey.RoleId);

            }


            unitOfWork.BeginTransaction();
            if (request.CheckModuleList != null && request.CheckModuleList.Count >= 0)
            {
                string roleType = EnumRoleType.Admin.ToString();
                //获取当前系统RoleType 为Admin的Role
                var q = unitOfWork.GetRepository<LTC_ROLES>().dbSet.Where(m => m.ORGID == request.OrgId && m.ROLETYPE == roleType && m.SYSTYPE == SecurityHelper.CurrentPrincipal.CurrentLoginSys);
                var adminRole = q.FirstOrDefault();
                var moduleIDs = request.CheckModuleList.Select(m => m.moduleId).ToArray();
                var moduleList = unitOfWork.GetRepository<LTC_MODULES>().dbSet.Where(m => moduleIDs.Contains(m.MODULEID)).ToList();

                if (adminRole != null)
                {
                    var moduleListByAdmin = adminRole.LTC_MODULES.ToList();
                    var changeModuleListByUnCheck = moduleListByAdmin.Where(m => !request.CheckModuleList.Any(c => c.moduleId == m.MODULEID)).ToList();
                    var roleList = unitOfWork.GetRepository<LTC_ROLES>().dbSet.Where(m => m.ORGID == request.OrgId).ToList();
                    roleList.ForEach(m =>
                    {
                        changeModuleListByUnCheck.ForEach(c =>
                        {
                            m.LTC_MODULES.Remove(c);
                        });
                    });
                    moduleList.ForEach(m => adminRole.LTC_MODULES.Add(m));
                }
                else
                {
                    LTC_ROLES role = new LTC_ROLES();
                    role.ROLEID = request.RoleId;
                    role.ORGID = request.OrgId;
                    role.ROLENAME = string.Format("Admin Role By OrgID: {0}", request.OrgId);
                    role.ROLETYPE = EnumRoleType.Admin.ToString();
                    role.SYSTYPE = SecurityHelper.CurrentPrincipal.CurrentLoginSys;
                    role.STATUS = true;
                    moduleList.ForEach(m => role.LTC_MODULES.Add(m));
                    unitOfWork.GetRepository<LTC_ROLES>().Insert(role);
                }
            }
            var response = base.Save<LTC_ORG, Organization>(request, (q) => q.ORGID == request.OrgId);
            unitOfWork.Commit();
            return response;
        }

        public BaseResponse DeleteOrg(string orgID)
        {
            var model = unitOfWork.GetRepository<LTC_ORG>().dbSet.Where(m => m.ORGID == orgID).ToList();
            if (model.Count > 0)
            {
                if (model[0].LTC_USERS.Count > 0)
                {
                    return new BaseResponse<LTC_ORG> { ResultMessage = "該機構無法刪除，請先刪除該機構下的所有用戶" };
                }
                else
                {
                    var response = base.Delete<LTC_ORG>(orgID);
                    response.ResultMessage = "刪除成功";
                    response.ResultCode = 1;
                    return response;
                }
            }
            else
            {
                return new BaseResponse<LTC_ORG> { ResultMessage = "刪除失敗" };
            }
        }
        #endregion

        #region 床位
        public BaseResponse<IList<BedBasic>> QueryBedBasic(BaseRequest<BedBasicFilter> request)
        {
            BaseResponse<IList<BedBasic>> response = base.Query<LTC_BEDBASIC, BedBasic>(request, (q) =>
            {
                if (!string.IsNullOrEmpty(request.Data.OrgId))
                {
                    q = q.Where(m => m.ORGID == request.Data.OrgId);
                }
                if (!string.IsNullOrEmpty(request.Data.BedNo))
                {
                    q = q.Where(m => m.BEDNO == request.Data.BedNo);
                }
                q = q.OrderBy(m => m.BEDNO);
                return q;
            });
            return response;
        }

        public BaseResponse<IList<BedBasic>> QueryBedBasicExtend(BaseRequest<BedBasicFilter> request)
        {
            BaseResponse<IList<BedBasic>> response = new BaseResponse<IList<BedBasic>>();
            var q = from it in unitOfWork.GetRepository<LTC_BEDBASIC>().dbSet
                    join n in
                        (from a in unitOfWork.GetRepository<LTC_IPDREG>().dbSet
                         join m in unitOfWork.GetRepository<LTC_REGFILE>().dbSet on a.REGNO equals m.REGNO
                         select new { a.REGNO, a.FEENO, a.CTRLFLAG, a.INDATE, m.SEX, m.NAME, m.HABIT, m.CONSTELLATIONS, m.LANGUAGE, m.MERRYFLAG, m.RELIGIONCODE, m.AGE, m.BLOODTYPE }) on it.FEENO equals n.FEENO into nns
                    join f in unitOfWork.GetRepository<LTC_ORGFLOOR>().dbSet on it.FLOOR equals f.FLOORID into ffs
                    join r in unitOfWork.GetRepository<LTC_ORGROOM>().dbSet on it.ROOMNO equals r.ROOMNO into rrs
                    join d in unitOfWork.GetRepository<LTC_DEPTFILE>().dbSet on it.DEPTNO equals d.DEPTNO into dds
                    join o in unitOfWork.GetRepository<LTC_ORG>().dbSet on it.ORGID equals o.ORGID into oos
                    from nn in nns.DefaultIfEmpty()
                    from ff in ffs.DefaultIfEmpty()
                    from rr in rrs.DefaultIfEmpty()
                    from dd in dds.DefaultIfEmpty()
                    from oo in oos.DefaultIfEmpty()
                    select new BedBasic
                    {
                        BedNo = it.BEDNO,
                        BedClass = it.BEDCLASS,
                        BedDesc = it.BEDDESC,
                        BedKind = it.BEDKIND,
                        BedStatus = it.BEDSTATUS,
                        BedType = it.BEDTYPE,
                        DeptName = dd.DEPTNAME,
                        Floor = ff.FLOORID,
                        FloorName = ff.FLOORNAME,
                        RoomName = rr.ROOMNAME,
                        RoomNo = rr.ROOMNO,
                        SexType = it.SEXTYPE,
                        OrgName = oo.ORGNAME,
                        OrgId = oo.ORGID,
                        InsbedFlag = it.INSBEDFLAG,
                        Prestatus = it.PRESTATUS,
                        Status = it.STATUS,
                        FEENO = nn.FEENO,
                        ResidentName = nn.NAME,
                        Habit = nn.HABIT,
                        Language = nn.LANGUAGE,
                        Merryflag = nn.MERRYFLAG,
                        Constellations = nn.CONSTELLATIONS,
                        Religioncode = nn.RELIGIONCODE,
                        Bloodtype = nn.BLOODTYPE,
                        Age = nn.AGE,
                        Sex = nn.SEX,
                        Ctrflag = nn.CTRLFLAG,
                        InDete = nn.INDATE,
                        RegNo = nn.REGNO
                    };

            if (!string.IsNullOrEmpty(request.Data.OrgId))
            {
                q = q.Where(p => p.OrgId == request.Data.OrgId);
            }
            if (!string.IsNullOrEmpty(request.Data.KeyWords))
            {
                q = q.Where(p => p.Floor.Contains(request.Data.KeyWords) || p.RoomName.Contains(request.Data.KeyWords));
            }

            q = q.Where(p => p.BedStatus != "N");//过滤掉关账状态zhongyh


            q = q.OrderByDescending(m => m.BedNo);
            response.RecordsCount = q.Count();
            if (request != null && request.PageSize > 0)
            {
                response.Data = q.Skip((request.CurrentPage - 1) * request.PageSize).Take(request.PageSize).ToList();
                response.PagesCount = GetPagesCount(request.PageSize, response.RecordsCount);
            }
            else
            {
                response.Data = q.ToList();
            }
            return response;
        }

        public BaseResponse<BedBasic> GetBedBasic(string bedNo)
        {
            return base.Get<LTC_BEDBASIC, BedBasic>((q) => q.BEDNO == bedNo);
        }

        public BaseResponse<BedBasic> SaveBedBasic(BedBasic request)
        {
            BaseResponse<BedBasic> response = new BaseResponse<BedBasic>();
            var cm = Mapper.CreateMap<BedBasic, LTC_BEDBASIC>();
            var model = unitOfWork.GetRepository<LTC_BEDBASIC>().dbSet.FirstOrDefault(m => m.BEDNO == request.BedNo);
            if (model == null)
            {
                model = Mapper.Map<LTC_BEDBASIC>(request);
                unitOfWork.GetRepository<LTC_BEDBASIC>().Insert(model);


            }
            else
            {
                Mapper.Map(request, model);
                unitOfWork.GetRepository<LTC_BEDBASIC>().Update(model);
                var regList = unitOfWork.GetRepository<LTC_IPDREG>().dbSet.Where(o => o.BEDNO == model.BEDNO).ToList();
                if (regList != null)
                {
                    regList.ForEach(item =>
                    {
                        item.ROOMNO = model.ROOMNO;
                        item.FLOOR = model.FLOOR;
                        unitOfWork.GetRepository<LTC_IPDREG>().Update(item);
                    });
                }
            }
            unitOfWork.Save();
            response.Data = request;
            return response;
        }

        public BaseResponse UpdateBedBasic(BedBasic request)
        {
            BaseResponse response = new BaseResponse();
            LTC_BEDBASIC bedBasic = unitOfWork.GetRepository<LTC_BEDBASIC>().dbSet.Where(x => x.BEDNO == request.BedNo).FirstOrDefault();
            if (bedBasic != null)
            {
                bedBasic.FEENO = request.FEENO;
                bedBasic.BEDSTATUS = request.BedStatus;
                unitOfWork.GetRepository<LTC_BEDBASIC>().Update(bedBasic);
                unitOfWork.Save();
            }
            return response;
        }

        public BaseResponse DeleteBedBasic(string bedNo)
        {
            return base.Delete<LTC_BEDBASIC>(bedNo);
        }
        #endregion

        #region 用戶
        public BaseResponse<IList<User>> QueryUser(BaseRequest<UserFilter> request)
        {
            var response = base.Query<LTC_USERS, User>(request, (q) =>
            {
                if (!string.IsNullOrEmpty(request.Data.LoginName) && !string.IsNullOrEmpty(request.Data.EmpNo))
                {
                    q = q.Where(m => m.LOGONNAME.Contains(request.Data.LoginName) || m.EMPNO.Contains(request.Data.EmpNo));
                }
                if (request != null && !string.IsNullOrEmpty(request.Data.OrgId))
                {
                    q = q.Where(m => m.LTC_ORG.Any(o => o.ORGID == request.Data.OrgId));
                }
                if (request != null && !string.IsNullOrEmpty(request.Data.LoginName))
                {
                    q = q.Where(m => m.LOGONNAME == request.Data.LoginName);
                }
                q = q.OrderByDescending(m => m.CREATEBY);
                return q;
            });
            return response;
        }

        /// <summary>
        /// 新增或修改 用戶賬號信息時，校驗 員工號與登錄賬號是否存在
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public BaseResponse CheckUserEmpAndLogName(BaseRequest<UserFilter> request)
        {
            //根据EMPNO判断员工 是否已经关联过 登录账号  ResultCode 1:empNo 已绑定账号  2：loginName重复
            BaseResponse response = new BaseResponse();
            response.ResultCode = 0;
            List<LTC_USERS> allSameEmp = new List<LTC_USERS>();
            //UserId>0 編輯用戶 UserId=0 新增
            if (request.Data.UserId > 0)
            {
                if (unitOfWork.GetRepository<LTC_USERS>().dbSet.Where(x => x.USERID != request.Data.UserId && x.EMPNO.Equals(request.Data.EmpNo) && x.LTC_ORG.Where(o => o.ORGID.Contains(request.Data.OrgId)).Count() > 0).Count() > 0)
                {
                    response.ResultCode = 1;
                }
                else if (unitOfWork.GetRepository<LTC_USERS>().dbSet.Where(x => x.USERID != request.Data.UserId && x.LOGONNAME.Equals(request.Data.LoginName) && x.LTC_ORG.Where(o => o.ORGID.Contains(request.Data.OrgId)).Count() > 0).Count() > 0)
                {
                    response.ResultCode = 2;
                }
            }
            else
            {
                if (unitOfWork.GetRepository<LTC_USERS>().dbSet.Where(x => x.EMPNO.Equals(request.Data.EmpNo) && x.LTC_ORG.Where(o => o.ORGID.Contains(request.Data.OrgId)).Count() > 0).Count() > 0)
                {
                    response.ResultCode = 1;
                }
                else if (unitOfWork.GetRepository<LTC_USERS>().dbSet.Where(x => x.LOGONNAME.Equals(request.Data.LoginName) && x.LTC_ORG.Where(o => o.ORGID.Contains(request.Data.OrgId)).Count() > 0).Count() > 0)
                {
                    response.ResultCode = 2;
                }
            }
            return response;
        }

        public BaseResponse<IList<User>> QueryUserExtend(BaseRequest<UserFilter> request)
        {
            BaseResponse<IList<User>> response = new BaseResponse<IList<User>>();


            var q = (from u in unitOfWork.GetRepository<LTC_USERS>().dbSet
                     join e in unitOfWork.GetRepository<LTC_EMPFILE>().dbSet on u.EMPNO equals e.EMPNO
                     select new
                     {
                         UserId = u.USERID,
                         LogonName = u.LOGONNAME,
                         Pwd = u.PWD,
                         OrgList = u.LTC_ORG,
                         EmpNo = e.EMPNO,
                         EmpName = e.EMPNAME,
                         EmpGroup = e.EMPGROUP,
                         CreateDate = u.CREATEDATE,
                         JobTitle = e.JOBTITLE,
                         JobType = e.JOBTYPE,
                         PwdExpDate = u.PWDEXPDATE,
                         RoleId = u.LTC_ROLES,
                         Status = u.STATUS
                     }).AsEnumerable().Select(o => new User
                     {
                         UserId = o.UserId,
                         LogonName = o.LogonName,
                         Pwd = o.Pwd,
                         OrgIds = o.OrgList.Select(x => x.ORGID).ToArray(),
                         EmpNo = o.EmpNo,
                         EmpName = o.EmpName,
                         EmpGroup = o.EmpGroup,
                         CreateDate = o.CreateDate,
                         JobTitle = o.JobTitle,
                         JobType = o.JobType,
                         PwdExpDate = o.PwdExpDate,
                         RoleId = o.RoleId.Select(x => x.ROLEID).ToArray(),
                         RoleType = o.RoleId.Select(x => x.ROLETYPE).ToArray(),
                         SysType = o.RoleId.Select(x => x.SYSTYPE).ToArray(),
                         DCRoleType = o.RoleId.Where(x => x.SYSTYPE == "DC").Select(x => x.ROLETYPE).ToArray(),
                         LTCRoleType = o.RoleId.Where(x => x.SYSTYPE == "LC").Select(x => x.ROLETYPE).ToArray(),
                         Status = o.Status
                     });

            if (!string.IsNullOrEmpty(request.Data.LoginName) && !string.IsNullOrEmpty(request.Data.EmpNo))
            {
                q = q.Where(m => m.LogonName.Contains(request.Data.LoginName) || m.EmpName.Contains(request.Data.EmpNo));
            }
            else
            {
                if (request != null && !string.IsNullOrEmpty(request.Data.LoginName))
                {
                    q = q.Where(m => m.LogonName == request.Data.LoginName);
                }
                if (request != null && !string.IsNullOrEmpty(request.Data.Password))
                {
                    q = q.Where(m => m.Pwd == request.Data.Password);
                }
                if (request != null && !string.IsNullOrEmpty(request.Data.OrgId))
                {
                    q = q.Where(m => m.OrgIds.Contains(request.Data.OrgId));
                }
                else if (SecurityHelper.CurrentPrincipal != null)
                {
                    //q = q.Where(m => m.OrgId == SecurityHelper.CurrentPrincipal.OrgId);
                }
            }
            q = q.OrderByDescending(m => m.CreateDate);
            response.RecordsCount = q.Count();
            List<User> list = null;
            if (request != null && request.PageSize > 0)
            {
                list = q.Skip((request.CurrentPage - 1) * request.PageSize).Take(request.PageSize).ToList();
                response.PagesCount = GetPagesCount(request.PageSize, response.RecordsCount);
            }
            else
            {
                list = q.ToList();
            }
            if (list.Count > 0)
            {
                list[0].OrgId = list[0].OrgIds[0];
            }
            response.Data = list;
            return response;
        }

        public BaseResponse<User> GetUserwithempno(string EMPNO)
        {
            Mapper.CreateMap<LTC_USERS, User>();
            BaseResponse<User> response = new BaseResponse<User>();
            var findItem = unitOfWork.GetRepository<LTC_USERS>().dbSet.FirstOrDefault(o => o.EMPNO == EMPNO);
            if (findItem != null)
            {
                response.Data = Mapper.Map<User>(findItem);
                response.Data.RoleId = findItem.LTC_ROLES.Select(o => o.ROLEID).ToArray();
            }
            return response;
        }
        /// <summary>
        /// 2016/8/31 modify by Lei:获取role数组时，增加CurrentLoginSys条件
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        public BaseResponse<User> GetUser(int userId)
        {
            Mapper.CreateMap<LTC_USERS, User>();
            BaseResponse<User> response = new BaseResponse<User>();
            var findItem = unitOfWork.GetRepository<LTC_USERS>().dbSet.FirstOrDefault(o => o.USERID == userId);
            if (findItem != null)
            {
                response.Data = Mapper.Map<User>(findItem);
                response.Data.RoleId = findItem.LTC_ROLES.Where(x => x.SYSTYPE == SecurityHelper.CurrentPrincipal.CurrentLoginSys).Select(o => o.ROLEID).ToArray();
                if (findItem.LTC_ORG.Count > 0)
                {
                    response.Data.OrgId = findItem.LTC_ORG.Select(o => o.ORGID).ToArray()[0];
                }
            }
            return response;
            //return base.Get<LTC_USERS, User>((q) => q.USERID == userId);
        }

        public BaseResponse<User> SaveUser(User request)
        {
            BaseResponse<User> response = new BaseResponse<User>();
            var cm = Mapper.CreateMap<User, LTC_USERS>();
            var model = unitOfWork.GetRepository<LTC_USERS>().dbSet.FirstOrDefault(m => m.USERID == request.UserId);
            if (model == null)
            {
                model = Mapper.Map<LTC_USERS>(request);
                unitOfWork.GetRepository<LTC_USERS>().Insert(model);

                if (!string.IsNullOrWhiteSpace(SecurityHelper.CurrentPrincipal.OrgId))
                {
                    var org = unitOfWork.GetRepository<LTC_ORG>().dbSet.Where(o => o.ORGID == request.OrgId).ToList();
                    model.LTC_ORG.Clear();
                    org.ForEach(item =>
                    {
                        model.LTC_ORG.Add(item);
                    });
                }
            }
            else
            {
                Mapper.Map(request, model);
                unitOfWork.GetRepository<LTC_USERS>().Update(model);
                var strSql = String.Format("update LTC_USERORG set ORGID='{0}' where USERID='{1}'", request.OrgId, model.USERID);
                unitOfWork.GetRepository<LTC_USERS>().ExecuteSqlCommand(strSql);
            }
            var roles = unitOfWork.GetRepository<LTC_ROLES>().dbSet.Where(o => request.RoleId.Contains(o.ROLEID)).ToList();
            model.LTC_ROLES.Clear();
            roles.ForEach(item =>
            {
                model.LTC_ROLES.Add(item);
            });
            unitOfWork.Save();
            response.Data = request;
            return response;
            //return base.Save<LTC_USERS, User>(request, (q) => q.USERID == request.UserId);
        }

        public BaseResponse DeleteUser(int userID)
        {
            BaseResponse response = new BaseResponse();
            try
            {
                //登陸中的賬號本身不能刪除
                if (userID == SecurityHelper.CurrentPrincipal.UserId)
                {
                    response.ResultCode = -2;
                    response.ResultMessage = "無法刪除登陸中的賬號";
                    return response;
                }

                //查詢刪除用戶信息
                var findItem = unitOfWork.GetRepository<LTC_USERS>().dbSet.FirstOrDefault(o => o.USERID == userID);
                var roleType = "";
                if (findItem.LTC_ROLES.Count > 0)
                {
                    var _roleTypeC = SecurityHelper.CurrentPrincipal.RoleType;
                    //獲取多角色的最高權限
                    if (_roleTypeC.Count() > 0)
                    {
                        foreach (var item in _roleTypeC)
                        {
                            if (item.ToString() == "SuperAdmin")
                            {
                                roleType = "SuperAdmin";
                                break;
                            }
                            if (item.ToString() == "Admin")
                            {
                                roleType = "Admin";
                                break;
                            }
                            if (item.ToString() == "Normal")
                            {
                                roleType = "Normal";
                                break;
                            }
                        }
                        if (roleType == "Admin")
                        {
                            foreach (var item in findItem.LTC_ROLES)
                            {
                                if (item.ROLETYPE == "SuperAdmin")
                                {
                                    response.ResultCode = -1;
                                    response.ResultMessage = "你沒有權限刪除該用戶";
                                    return response;
                                }
                            }
                        }
                        else if (roleType == "Normal")
                        {
                            foreach (var item in findItem.LTC_ROLES)
                            {
                                if (item.ROLETYPE == "SuperAdmin")
                                {
                                    response.ResultCode = -1;
                                    response.ResultMessage = "你沒有權限刪除該用戶";
                                    return response;
                                }
                                else if (item.ROLETYPE == "Admin")
                                {
                                    response.ResultCode = -1;
                                    response.ResultMessage = "你沒有權限刪除該用戶";
                                    return response;
                                }
                            }
                        }
                    }
                }



                unitOfWork.BeginTransaction();
                string strSql = String.Format("delete from LTC_USERORG where USERID='{0}'", userID);
                unitOfWork.GetRepository<LTC_USERS>().ExecuteSqlCommand(strSql);

                strSql = String.Format("delete from LTC_USERROLES where USERID='{0}'", userID);
                unitOfWork.GetRepository<LTC_USERS>().ExecuteSqlCommand(strSql);

                unitOfWork.GetRepository<LTC_USERS>().Delete(userID);

                if (!String.IsNullOrEmpty(findItem.EMPNO))
                {
                    strSql = String.Format("delete from LTC_EMPFILE where EMPNO='{0}'", findItem.EMPNO);
                    unitOfWork.GetRepository<LTC_EMPFILE>().ExecuteSqlCommand(strSql);
                    //unitOfWork.GetRepository<LTC_EMPFILE>().Delete(findItem.EMPNO);
                }
                unitOfWork.Save();
                response.ResultCode = 1;
                response.ResultMessage = "刪除成功";
                return response;
            }
            catch (Exception ex)
            {
                response.ResultCode = 0;
                response.ResultMessage = "刪除異常";
                return response;
                throw ex;
            }
        }



        public BaseResponse ResetPassword(string orgID, string logonName, string password)
        {
            BaseResponse response = new BaseResponse();
            var model = unitOfWork.GetRepository<LTC_USERS>().dbSet.FirstOrDefault(m => m.LTC_ORG.Any(it => it.ORGID == orgID) && m.LOGONNAME == logonName);
            if (model != null)
            {
                model.PWD = password;
                model.UPDATEDATE = DateTime.Now;
                unitOfWork.GetRepository<LTC_USERS>().Update(model);
                unitOfWork.Save();
            }
            else
            {
                response.ResultCode = -1;
                response.ResultMessage = "查詢不到賬號";
            }
            return response;
        }
        public BaseResponse ChangePassWord(string orgID, string logonName, string oldPassword, string newPassword)
        {
            BaseResponse response = new BaseResponse();
            var model = unitOfWork.GetRepository<LTC_USERS>().dbSet.FirstOrDefault(m => m.LTC_ORG.Any(it => it.ORGID == orgID) && m.LOGONNAME == logonName);
            if (model != null)
            {
                if (model.PWD == oldPassword)
                {
                    model.PWD = newPassword;
                    model.UPDATEDATE = DateTime.Now;
                    unitOfWork.GetRepository<LTC_USERS>().Update(model);
                    unitOfWork.Save();
                }
                else
                {
                    response.ResultCode = -1;
                    response.ResultMessage = "舊密碼輸入錯誤";
                }
               
            }
            else
            {
                response.ResultCode = -1;
                response.ResultMessage = "用戶信息為空";
            }
            return response;
        }
        #endregion

        #region 角色
        public BaseResponse<IList<Role>> QueryRole(BaseRequest<RoleFilter> request)
        {
            BaseResponse<IList<Role>> response = new BaseResponse<IList<Role>>();
            var q = from of in unitOfWork.GetRepository<LTC_ROLES>().dbSet
                    join o in unitOfWork.GetRepository<LTC_ORG>().dbSet on of.ORGID equals o.ORGID into ofos
                    from ofo in ofos.DefaultIfEmpty()
                    select new
                    {
                        Role = of,
                        OrgName = ofo.ORGNAME
                    };
            if (request != null && !string.IsNullOrEmpty(request.Data.CurrentLoginSys))
            {
                q = q.Where(m => m.Role.SYSTYPE == request.Data.CurrentLoginSys);
            }
            if (request != null && !string.IsNullOrEmpty(request.Data.OrgId))
            {
                q = q.Where(m => m.Role.ORGID == request.Data.OrgId);
            }
            if (request != null && !string.IsNullOrEmpty(request.Data.RoleName))
            {
                q = q.Where(m => m.Role.ROLENAME == request.Data.RoleName);
            }
            if (request != null && !string.IsNullOrEmpty(request.Data.RoleType))
            {
                q = q.Where(m => m.Role.ROLETYPE == request.Data.RoleType);
            }
            if (request != null && request.Data.Status.HasValue)
            {
                q = q.Where(m => m.Role.STATUS == request.Data.Status);
            }

            q = q.Where(m => m.Role.ROLETYPE != "SuperAdmin");//超级管理员角色不显示不查询不编辑zhongyh

            q = q.OrderByDescending(m => m.Role.CREATEDATE);
            response.RecordsCount = q.Count();
            Action<IList> mapperResponse = (IList list) =>
            {
                response.Data = new List<Role>();
                foreach (dynamic item in list)
                {
                    Role newItem = Mapper.DynamicMap<Role>(item.Role);
                    newItem.OrgName = item.OrgName;
                    response.Data.Add(newItem);
                }

            };
            if (request != null && request.PageSize > 0)
            {
                var list = q.Skip((request.CurrentPage - 1) * request.PageSize).Take(request.PageSize).ToList();
                response.PagesCount = GetPagesCount(request.PageSize, response.RecordsCount);
                mapperResponse(list);
            }
            else
            {
                var list = q.ToList();
                mapperResponse(list);
            }
            return response;
        }

        public BaseResponse<Role> GetRole(string roleID)
        {
            return base.Get<LTC_ROLES, Role>((q) => q.ROLEID == roleID && q.SYSTYPE == SecurityHelper.CurrentPrincipal.CurrentLoginSys);
        }

        public BaseResponse<Role> SaveRole(Role request)
        {

            BaseResponse<Role> response = new BaseResponse<Role>();
            var cm = Mapper.CreateMap<Role, LTC_ROLES>();
            var model = unitOfWork.GetRepository<LTC_ROLES>().dbSet.FirstOrDefault(m => m.ROLEID == request.RoleId);
            if (model == null)
            {
                request.SysType = SecurityHelper.CurrentPrincipal.CurrentLoginSys;
                model = Mapper.Map<LTC_ROLES>(request);
                model.CREATEDATE = DateTime.Now;
                model.CREATEBY = SecurityHelper.CurrentPrincipal.EmpNo;
                unitOfWork.GetRepository<LTC_ROLES>().Insert(model);
            }
            else
            {
                Mapper.Map(request, model);
                model.UPDATEDATE = DateTime.Now;
                model.UPDATEBY = SecurityHelper.CurrentPrincipal.EmpNo;
                unitOfWork.GetRepository<LTC_ROLES>().Update(model);
            }
            if (request.CheckModuleList != null)
            {
                var moduleIdList = request.CheckModuleList.Select(m => m.moduleId).ToList();
                var moduleList = unitOfWork.GetRepository<LTC_MODULES>().dbSet.Where(m => moduleIdList.Contains(m.MODULEID)).ToList();
                model.LTC_MODULES.Clear();
                moduleList.ForEach(item =>
                {
                    model.LTC_MODULES.Add(item);
                });
            }
            unitOfWork.Save();
            response.Data = request;
            request.RoleId = model.ROLEID;
            return response;
        }

        public BaseResponse DeleteRole(string roleID)
        {
            var model = unitOfWork.GetRepository<LTC_ROLES>().dbSet.Where(m => m.ROLEID == roleID).ToList();
            if (model.Count > 0)
            {
                if (model[0].LTC_USERS.Count > 0)
                {
                    return new BaseResponse<LTC_ROLES> { ResultMessage = "該角色無法刪除，請先刪除該角色下的所有用戶" };
                }
                else
                {
                    var strSql = String.Format("delete from LTC_ROLEMODULES where roleId='{0}'", roleID);
                    unitOfWork.GetRepository<LTC_ROLES>().ExecuteSqlCommand(strSql);
                    strSql = String.Format("delete from LTC_USERROLES where roleId='{0}'", roleID);
                    unitOfWork.GetRepository<LTC_ROLES>().ExecuteSqlCommand(strSql);
                    var response = base.Delete<LTC_ROLES>(roleID);
                    response.ResultMessage = "刪除成功";
                    response.ResultCode = 1;
                    return response;
                }
            }
            else
            {
                return new BaseResponse<LTC_ROLES> { ResultMessage = "刪除失敗" };
            }

        }

        public IEnumerable<Module> GetRoleModule(RoleFilter request)
        {
            List<LTC_MODULES> modules = new List<LTC_MODULES>();
            var q = unitOfWork.GetRepository<LTC_ROLES>().dbSet.Where(m => m.STATUS == true && m.SYSTYPE == SecurityHelper.CurrentPrincipal.CurrentLoginSys);
            if (request != null && !string.IsNullOrEmpty(request.OrgId))
            {
                q = q.Where(m => m.ORGID == request.OrgId);
            }
            if (request != null && request.RoleId != null && request.RoleId.Length > 0)
            {
                q = q.Where(m => request.RoleId.Contains(m.ROLEID));
            }
            if (request != null && !string.IsNullOrEmpty(request.RoleType))
            {
                q = q.Where(m => m.ROLETYPE == request.RoleType);
            }

            //超级管理直接返回全部菜单zhongyh
            if (request != null && !string.IsNullOrEmpty(request.RoleType))
            {
                if (request.RoleType.Equals(EnumRoleType.SuperAdmin.ToString()))
                {
                    modules = unitOfWork.GetRepository<LTC_MODULES>().dbSet.Where(m => m.SYSTYPE == SecurityHelper.CurrentPrincipal.CurrentLoginSys).ToList();
                    Mapper.CreateMap<LTC_MODULES, Module>();
                    return Mapper.Map<IEnumerable<Module>>(modules);
                }

            }

            var roleList = q.ToList();
            if (roleList != null && roleList.Count > 0)
            {
                if (roleList.Where(m => m.ROLETYPE.Contains(EnumRoleType.SuperAdmin.ToString())).Count() > 0)
                {
                    modules = unitOfWork.GetRepository<LTC_MODULES>().dbSet.Where(m => m.SYSTYPE == SecurityHelper.CurrentPrincipal.CurrentLoginSys).ToList();
                }
                else
                {
                    if (roleList.Count > 1)
                    {
                        foreach (var m in roleList)
                        {
                            modules.AddRange(m.LTC_MODULES);
                        }
                        modules = modules.Distinct().ToList();
                    }
                    else
                    {
                        modules = roleList.FirstOrDefault().LTC_MODULES.ToList();
                    }
                }
            }
            else
            {
                return new List<Module>();
            }
            Mapper.CreateMap<LTC_MODULES, Module>();
            return Mapper.Map<IEnumerable<Module>>(modules);
        }

        public List<MenuTree> GetMenus(RoleFilter request)
        {
            var list = GetRoleModule(request).ToList();
            return CreateTree(list, "");
        }

        private List<MenuTree> CreateTree(List<Module> menus, string pId)
        {
            var list = menus.Where(o => o.SuperModuleId == pId).ToList();
            if (!list.Any())
            {
                return null;
            }
            return list.Select(item => new MenuTree()
            {
                name = item.ModuleName,
                url = item.Url,
                nodes = CreateTree(menus, item.ModuleId)
            }).ToList();
        }

        private void LoadTree(TreeNode parentNode, IList<Module> modules, IEnumerable<Module> modulesByRole, string moduleId)
        {
            var nodes = modules.Where(m => m.SuperModuleId == moduleId).ToList();
            if (nodes.Count > 0 && parentNode.nodes == null)
            {
                parentNode.nodes = new List<TreeNode>();
            }
            foreach (var item in nodes)
            {
                var newNode = new TreeNode();
                newNode.moduleId = item.ModuleId;
                newNode.text = item.ModuleName;
                newNode.href = item.Url;
                newNode.state = new State { @checked = modulesByRole.Any(m => m.ModuleId == item.ModuleId) };
                parentNode.nodes.Add(newNode);
                LoadTree(newNode, modules, modulesByRole, item.ModuleId);
            }
        }

        public BaseResponse<IList<TreeNode>> GetModuleByRole(BaseRequest<RoleFilter> requestByRole, BaseRequest<RoleFilter> requestByTree)
        {
            BaseResponse<IList<TreeNode>> response = new BaseResponse<IList<TreeNode>>();
            var moduleList = this.QueryModule(requestByTree);
            var moduleListByRole = this.GetRoleModule(requestByRole.Data);
            TreeNode rootNode = new TreeNode();
            LoadTree(rootNode, moduleList.Data, moduleListByRole, "00");
            response.Data = rootNode.nodes;
            return response;
        }

        public BaseResponse<List<User>> GetUsreByRoleType(string orgId, string roleType)
        {
            BaseResponse<List<User>> response = new BaseResponse<List<User>>();
            var roleSet = unitOfWork.GetRepository<LTC_ROLES>().dbSet;
            var userSet = unitOfWork.GetRepository<LTC_USERS>().dbSet;
            var q = from a in roleSet
                    from b in userSet
                    where a.ORGID == orgId && a.ROLETYPE == roleType && a.LTC_USERS.Any(it => it.USERID == b.USERID)
                    select b;
            List<LTC_USERS> list = q.Distinct().ToList();
            Mapper.CreateMap<LTC_USERS, User>();
            response.Data = Mapper.Map<List<User>>(list);
            return response;
        }


        #endregion

        #region 模塊
        public BaseResponse<IList<Module>> QueryModule(BaseRequest<ModuleFilter> request)
        {
            var response = base.Query<LTC_MODULES, Module>(request, (q) =>
            {
                q = q.Where(m => m.SYSTYPE == SecurityHelper.CurrentPrincipal.CurrentLoginSys);
                q = q.OrderBy(m => m.MODULEID);
                return q;
            });
            return response;
        }

        private BaseResponse<IList<Module>> QueryModule(BaseRequest<RoleFilter> request)
        {
            if (request != null && !string.IsNullOrEmpty(request.Data.OrgId) && !string.IsNullOrEmpty(request.Data.RoleType))
            {
                BaseResponse<IList<Module>> response = new BaseResponse<IList<Module>>();
                response.Data = (IList<Module>)this.GetRoleModule(request.Data);
                return response;
            }
            else
            {
                BaseRequest<ModuleFilter> requestByModule = new BaseRequest<ModuleFilter>();
                requestByModule.PageSize = 0;
                return this.QueryModule(requestByModule);
            }
        }

        public BaseResponse<Module> GetModule(string moduleID)
        {
            return base.Get<LTC_MODULES, Module>((q) => q.MODULEID == moduleID);
        }

        public BaseResponse<Module> SaveModule(Module request)
        {
            return base.Save<LTC_MODULES, Module>(request, (q) => q.MODULEID == request.ModuleId);
        }

        public BaseResponse DeleteModule(string moduleID)
        {
            return base.Delete<LTC_MODULES>(moduleID);
        }

        #endregion

        #region 員工
        public BaseResponse<IList<Employee>> QueryEmployee(BaseRequest<EmployeeFilter> request)
        {
            var response = base.Query<LTC_EMPFILE, Employee>(request, (q) =>
            {
                if (request != null && !string.IsNullOrEmpty(request.Data.EmpNo) && !string.IsNullOrEmpty(request.Data.EmpName))
                {
                    q = q.Where(m => m.EMPNO.Contains(request.Data.EmpNo) || m.EMPNAME.Contains(request.Data.EmpName));
                }
                else if (request != null && !string.IsNullOrEmpty(request.Data.EmpNo))
                {
                    q = q.Where(m => m.EMPNO == request.Data.EmpNo);
                }
                else if (request != null && !string.IsNullOrEmpty(request.Data.EmpName))
                {
                    q = q.Where(m => m.EMPNAME.Contains(request.Data.EmpName));
                }
                if (request != null && !string.IsNullOrEmpty(request.Data.EmpGroup))
                {
                    q = q.Where(m => m.EMPGROUP == request.Data.EmpGroup);
                }
                if (request != null && !string.IsNullOrEmpty(request.Data.OrgId))
                {
                    q = q.Where(m => m.ORGID == request.Data.OrgId);
                }
                if (request != null && !string.IsNullOrEmpty(request.Data.IdNo))
                {
                    q = q.Where(m => m.IDNO == request.Data.IdNo);
                }

                q = q.OrderByDescending(m => m.EMPNO);
                return q;
            });
            return response;
        }

        public BaseResponse<IList<Employee>> QueryUserUnionEmp(BaseRequest<EmployeeFilter> request)
        {
            var response = new BaseResponse<IList<Employee>>();
            var q = (from e in unitOfWork.GetRepository<LTC_EMPFILE>().dbSet where e.ORGID == request.Data.OrgId select new Employee { EmpName = e.EMPNAME, EmpNo = e.EMPNO }).Union(
                from u in unitOfWork.GetRepository<LTC_USERS>().dbSet where u.LTC_ORG.Any(p => p.ORGID == request.Data.OrgId) select new Employee { EmpName = u.LOGONNAME, EmpNo = u.EMPNO }
            );
            q = q.OrderByDescending(p => p.EmpName);
            response.RecordsCount = q.Count();
            List<Employee> list = null;
            if (request != null && request.PageSize > 0)
            {
                list = q.Skip((request.CurrentPage - 1) * request.PageSize).Take(request.PageSize).ToList();
                response.PagesCount = GetPagesCount(request.PageSize, response.RecordsCount);
            }
            else
            {
                list = q.ToList();
            }
            response.Data = list;


            return response;
        }

        public BaseResponse<Employee> GetEmployee(string empNo)
        {
            return base.Get<LTC_EMPFILE, Employee>((q) => q.EMPNO == empNo);
        }

        public BaseResponse<Employee> SaveEmployee(Employee request)
        {
            if (string.IsNullOrEmpty(request.EmpNo))
            {
                //如果為四位數字，則使用系統自動生成規則
                Regex regex=new Regex(@"^\d{4}$");
               string serialEmpNo =base.GenerateCode(request.OrgId, EnumCodeKey.StaffId);
                if(regex.IsMatch(serialEmpNo))
                {
                    //生成不同機構不同前綴
                    string empNoPrefix = base.GenerateCode(request.OrgId, EnumCodeKey.EmpPrefix);
                    request.EmpNo = empNoPrefix + serialEmpNo;
                }
                else
                {
                    request.EmpNo = serialEmpNo;
                }
                // request.EmpNo = base.GenerateCode(SecurityHelper.CurrentPrincipal.OrgId, EnumCodeKey.StaffId);
                //modify SecurityHelper.CurrentPrincipal.OrgId ->request.OrgId  生成EMPNo,修复超级管理员创建其他机构员工，生成错误EMPNO
               
            }
            var responseEmployee = base.Save<LTC_EMPFILE, Employee>(request, (q) => q.EMPNO == request.EmpNo && q.ORGID == request.OrgId);

            return base.Save<LTC_EMPFILE, Employee>(request, (q) => q.EMPNO == request.EmpNo && q.ORGID == request.OrgId);
        }

        public BaseResponse DeleteEmployee(string empNo,string orgId)
        {
            BaseResponse response=new BaseResponse();
            LTC_EMPFILE entity=unitOfWork.GetRepository<LTC_EMPFILE>().dbSet.Where(x=>x.EMPNO==empNo&&x.ORGID==orgId).FirstOrDefault();
            unitOfWork.GetRepository<LTC_EMPFILE>().Delete(entity);
             unitOfWork.Save();
            return response;
        }
        #endregion

        #region 部門
        public BaseResponse<IList<Dept>> QueryDept(BaseRequest<DeptFilter> request)
        {
            var response = base.Query<LTC_DEPTFILE, Dept>(request, (q) =>
            {
                if (!string.IsNullOrEmpty(request.Data.OrgId))
                {
                    q = q.Where(p => p.ORGID == request.Data.OrgId);
                }
                if (!string.IsNullOrEmpty(request.Data.DeptNo))
                {
                    q = q.Where(p => p.DEPTNO == request.Data.DeptNo);
                }
                q = q.OrderByDescending(m => m.UPDATEDATE);
                return q;
            });
            return response;
        }


        public BaseResponse<IList<Dept>> QueryDeptExtend(BaseRequest<DeptFilter> request)
        {
            var response = new BaseResponse<IList<Dept>>();
            var q = from it in unitOfWork.GetRepository<LTC_DEPTFILE>().dbSet
                    join n in unitOfWork.GetRepository<LTC_ORG>().dbSet on it.ORGID equals n.ORGID
                    select new Dept
                    {
                        DeptName = it.DEPTNAME,
                        DeptNo = it.DEPTNO,
                        OrgId = it.ORGID,
                        OrgName = n.ORGNAME,
                        Status = it.STATUS,
                        Remark = it.REMARK
                    };
            if (!string.IsNullOrEmpty(request.Data.OrgId))
            {
                q = q.Where(p => p.OrgId == request.Data.OrgId);
            }
            if (!string.IsNullOrEmpty(request.Data.DeptNo))
            {
                q = q.Where(p => p.DeptNo.Contains(request.Data.DeptNo) || p.DeptName.Contains(request.Data.DeptName));
            }

            response.RecordsCount = q.Count();
            response.Data = q.ToList();
            return response;
        }
        public BaseResponse<Dept> GetDept(string deptNo)
        {
            return base.Get<LTC_DEPTFILE, Dept>((q) => q.DEPTNO == deptNo);
        }

        public BaseResponse<Dept> SaveDept(Dept request)
        {
            return base.Save<LTC_DEPTFILE, Dept>(request, (q) => q.DEPTNO == request.DeptNo&&q.ORGID==request.OrgId);
        }

        public BaseResponse DeleteDept(string deptNo,string orgId)
        {
            BaseResponse response = new BaseResponse();
            LTC_DEPTFILE entity = unitOfWork.GetRepository<LTC_DEPTFILE>().dbSet.Where(x => x.DEPTNO == deptNo && x.ORGID == orgId).FirstOrDefault();
            unitOfWork.GetRepository<LTC_DEPTFILE>().Delete(entity);
            unitOfWork.Save();
            return response;
           // return base.Delete<LTC_DEPTFILE>(deptNo);
        }
        #endregion

        #region 集團
        public BaseResponse<IList<Groups>> QueryGroup(BaseRequest<GroupFilter> request)
        {
            var response = base.Query<LTC_GROUP, Groups>(request, (q) =>
            {
                if (request != null && !string.IsNullOrEmpty(request.Data.GroupName))
                {
                    q = q.Where(m => m.GROUPNAME.Contains(request.Data.GroupName));
                }
                q = q.OrderByDescending(m => m.GROUPNAME);
                return q;
            });
            return response;
        }

        public BaseResponse<Groups> GetGroup(string GroupId)
        {
            return base.Get<LTC_GROUP, Groups>((q) => q.GROUPID == GroupId);
        }

        public BaseResponse<Groups> SaveGroup(Groups request)
        {
            if (string.IsNullOrEmpty(request.GroupId))
            {
                request.GroupId = base.GenerateCode(SecurityHelper.CurrentPrincipal.OrgId, EnumCodeKey.GroupId);
                var newObj = base.Save<LTC_GROUP, Groups>(request, (q) => q.GROUPID == request.GroupId);
                base.Save<SYS_CODERULE, CodeRule>(
                    new CodeRule()
                    {
                        CodeKey = "OrgId",
                        OrgId = newObj.Data.GroupId,
                        Flag = "0",
                        FlagRule = "0",
                        GenerateRule = "{number:10}"
                    },
                    (q) => q.ORGID == request.GroupId && q.CODEKEY == "OrgId");
                return newObj;
            }
            return base.Save<LTC_GROUP, Groups>(request, (q) => q.GROUPID == request.GroupId);
        }

        public BaseResponse DeleteGroup(string GroupId)
        {
            BaseResponse reulstbase = new BaseResponse();
            var rows = base.Get<LTC_ORG, Organization>((q) => q.GROUPID == GroupId);
            if (rows.Data != null)
            {
                reulstbase.RecordsCount = 0;
                reulstbase.ResultCode = -1;
                reulstbase.ResultMessage = "要选择删除的集团存在下属机构不能直接删除！";
                return reulstbase;
            }
            else
            { return base.Delete<LTC_GROUP>(GroupId); }

        }



        #endregion

        #region 楼层
        public BaseResponse<IList<OrgFloor>> QueryOrgFloor(BaseRequest<OrgFloorFilter> request)
        {
            BaseResponse<IList<OrgFloor>> response = base.Query<LTC_ORGFLOOR, OrgFloor>(request, (q) =>
            {
                if (!string.IsNullOrEmpty(request.Data.OrgId))
                {
                    q = q.Where(m => m.ORGID == request.Data.OrgId);
                }
                if (!string.IsNullOrEmpty(request.Data.FloorId))
                {
                    q = q.Where(m => m.FLOORID == request.Data.FloorId);
                }
                q = q.OrderBy(m => m.ORGID);
                return q;
            });
            return response;
        }

        public BaseResponse<IList<OrgFloor>> QueryOrgFloorExtend(BaseRequest<OrgFloorFilter> request)
        {
            BaseResponse<IList<OrgFloor>> response = new BaseResponse<IList<OrgFloor>>();
            var q = from of in unitOfWork.GetRepository<LTC_ORGFLOOR>().dbSet
                    join o in unitOfWork.GetRepository<LTC_ORG>().dbSet on of.ORGID equals o.ORGID into ofos
                    from ofo in ofos.DefaultIfEmpty()
                    select new
                    {
                        OrgFloor = of,
                        OrgName = ofo.ORGNAME
                    };
            if (!string.IsNullOrEmpty(request.Data.FloorName))
            {
                q = q.Where(m => m.OrgFloor.FLOORNAME.Contains(request.Data.FloorName));
            }
            if (!string.IsNullOrEmpty(request.Data.OrgId))
            {
                q = q.Where(m => m.OrgFloor.ORGID == request.Data.OrgId);
            }
            q = q.OrderByDescending(m => m.OrgFloor.FLOORNAME);
            response.RecordsCount = q.Count();
            Action<IList> mapperResponse = (IList list) =>
            {
                response.Data = new List<OrgFloor>();
                foreach (dynamic item in list)
                {
                    OrgFloor newItem = Mapper.DynamicMap<OrgFloor>(item.OrgFloor);
                    newItem.OrgName = item.OrgName;
                    response.Data.Add(newItem);
                }

            };
            if (request != null && request.PageSize > 0)
            {
                var list = q.Skip((request.CurrentPage - 1) * request.PageSize).Take(request.PageSize).ToList();
                response.PagesCount = GetPagesCount(request.PageSize, response.RecordsCount);
                mapperResponse(list);
            }
            else
            {
                var list = q.ToList();
                mapperResponse(list);
            }
            return response;
        }

        public BaseResponse<OrgFloor> GetOrgFloor(string FloorId)
        {
            return base.Get<LTC_ORGFLOOR, OrgFloor>((q) => q.FLOORID == FloorId);
        }

        public BaseResponse<OrgFloor> SaveOrgFloor(OrgFloor request)
        {
            return base.Save<LTC_ORGFLOOR, OrgFloor>(request, (q) => q.FLOORID == request.FloorId);
        }

        public BaseResponse DeleteOrgFloor(string FloorId)
        {
            return base.Delete<LTC_ORGFLOOR>(FloorId);
        }
        #endregion

        #region 房间
        public BaseResponse<IList<OrgRoom>> QueryOrgRoom(BaseRequest<OrgRoomFilter> request)
        {
            BaseResponse<IList<OrgRoom>> response = base.Query<LTC_ORGROOM, OrgRoom>(request, (q) =>
            {
                if (!string.IsNullOrEmpty(request.Data.OrgId))
                {
                    q = q.Where(m => m.ORGID == request.Data.OrgId);
                }
                if (!string.IsNullOrEmpty(request.Data.RoomNo))
                {
                    q = q.Where(m => m.ROOMNO == request.Data.RoomNo);
                }
                q = q.OrderBy(m => m.ROOMNO);
                return q;
            });
            return response;
        }

        public BaseResponse<IList<OrgRoom>> QueryOrgRoomExtend(BaseRequest<OrgRoomFilter> request)
        {
            BaseResponse<IList<OrgRoom>> response = new BaseResponse<IList<OrgRoom>>();
            var q = from or in unitOfWork.GetRepository<LTC_ORGROOM>().dbSet.Where(m => m.ORGID == SecurityHelper.CurrentPrincipal.OrgId)
                    join of in unitOfWork.GetRepository<LTC_ORGFLOOR>().dbSet on or.FLOORID equals of.FLOORID into orofs
                    join o in unitOfWork.GetRepository<LTC_ORG>().dbSet on or.ORGID equals o.ORGID into oros
                    from orof in orofs.DefaultIfEmpty()
                    from oro in oros.DefaultIfEmpty()
                    select new
                    {
                        OrgRoom = or,
                        FloorName = orof.FLOORNAME,
                        RoomName = or.ROOMNAME,
                        OrgName = oro.ORGNAME,
                        FloorId = or.FLOORID

                    };
            if (!string.IsNullOrEmpty(request.Data.RoomName))
            {
                q = q.Where(m => m.RoomName.Contains(request.Data.RoomName));
            }
            if (!string.IsNullOrEmpty(request.Data.FloorId))
            {
                q = q.Where(m => m.FloorId == request.Data.FloorId);
            }
            if (!string.IsNullOrEmpty(request.Data.OrgId))
            {
                q = q.Where(m => m.OrgRoom.ORGID == request.Data.OrgId);
            }
            q = q.OrderBy(m => m.OrgRoom.ROOMNO);
            response.RecordsCount = q.Count();
            Action<IList> mapperResponse = (IList list) =>
            {
                response.Data = new List<OrgRoom>();
                foreach (dynamic item in list)
                {
                    OrgRoom newItem = Mapper.DynamicMap<OrgRoom>(item.OrgRoom);
                    newItem.RoomName = item.RoomName;
                    newItem.FloorName = item.FloorName;
                    newItem.OrgName = item.OrgName;
                    response.Data.Add(newItem);
                }

            };
            if (request != null && request.PageSize > 0)
            {
                var list = q.Skip((request.CurrentPage - 1) * request.PageSize).Take(request.PageSize).ToList();
                response.PagesCount = GetPagesCount(request.PageSize, response.RecordsCount);
                mapperResponse(list);
            }
            else
            {
                var list = q.ToList();
                mapperResponse(list);
            }
            return response;
        }

        public BaseResponse<OrgRoom> GetOrgRoom(string RoomNo)
        {
            return base.Get<LTC_ORGROOM, OrgRoom>((q) => q.ROOMNO == RoomNo);
        }

        public BaseResponse<OrgRoom> SaveOrgRoom(OrgRoom request)
        {
            return base.Save<LTC_ORGROOM, OrgRoom>(request, (q) => q.ROOMNO == request.RoomNo);
        }

        public BaseResponse DeleteOrgRoom(string RoomNo)
        {
            return base.Delete<LTC_ORGROOM>(RoomNo);
        }
        #endregion

        #region 字典
        public BaseResponse<IList<CodeFile>> QueryCodeFile(BaseRequest<CommonFilter> request)
        {
            var response = Query<LTC_CODEFILE_REF, CodeFile>(request, (q) =>
            {
                if (!string.IsNullOrEmpty(request.Data.Keywords))
                {
                    q = q.Where(m => m.ITEMTYPE.Contains(request.Data.Keywords) || m.TYPENAME.Contains(request.Data.Keywords));
                }
                q = q.OrderByDescending(m => m.ITEMTYPE);
                return q;
            });
            return response;
        }

        public BaseResponse<CodeFile> GetCodeFile(string id)
        {
            return base.Get<LTC_CODEFILE_REF, CodeFile>((q) => q.ITEMTYPE == id);
        }

        public BaseResponse<CodeFile> SaveCodeFile(CodeFile request)
        {
            return base.Save<LTC_CODEFILE_REF, CodeFile>(request, (q) => q.ITEMTYPE == request.ITEMTYPE);
        }

        public BaseResponse DeleteCodeFile(string id)
        {
            return base.Delete<LTC_CODEFILE_REF>(id);
        }

        public BaseResponse<IList<CodeDtl>> QueryCodeDtl(BaseRequest<CommonFilter> request)
        {
            var response = Query<LTC_CODEDTL_REF, CodeDtl>(request, (q) =>
            {
                if (!string.IsNullOrEmpty(request.Data.Keywords))
                {
                    q = q.Where(m => m.ITEMTYPE == request.Data.Keywords);
                }
                q = q.OrderByDescending(m => m.ORDERSEQ);
                return q;
            });
            return response;
        }

        public BaseResponse<CodeDtl> GetCodeDtl(string id, string type)
        {
            return base.Get<LTC_CODEDTL_REF, CodeDtl>((q) => q.ITEMTYPE == type && q.ITEMCODE == id);
        }

        public BaseResponse<CodeDtl> SaveCodeDtl(CodeDtl request)
        {
            return base.Save<LTC_CODEDTL_REF, CodeDtl>(request, (q) => q.ITEMTYPE == request.ITEMTYPE && q.ITEMCODE == request.ITEMCODE);
        }


        public int DeleteCodeDtl(string id, string type)
        {
            return base.Delete<LTC_CODEDTL_REF>(o => o.ITEMTYPE == type && o.ITEMCODE == id);
        }
        #endregion

        #region 評估模板管理
        public BaseResponse<LTC_Question> GetQue(int id)
        {
            return base.Get<LTC_QUESTION, LTC_Question>((q) => q.ID == id);
        }
        public BaseResponse<IList<LTC_Question>> QueryQueList(BaseRequest<QuestionFilter> request)
        {
            var response = base.Query<LTC_QUESTION, LTC_Question>(request, (q) =>
            {
                q = q.Where(m => m.ORGID == request.Data.OrgId);
                if (!string.IsNullOrEmpty(request.Data.Questionname))
                {
                    q = q.Where(m => m.QUESTIONNAME.Contains(request.Data.Questionname) || m.QUESTIONDESC.Contains(request.Data.QuestionDesc));
                }
                q = q.OrderByDescending(m => m.ID);
                return q;
            });
            return response;
        }
        public BaseResponse<IList<LTC_Question>> QueryEvalTempSetList(BaseRequest<QuestionFilter> request)
        {
            var response = base.Query<LTC_QUESTION, LTC_Question>(request, (q) =>
            {
                q = q.Where(m => m.ORGID == request.Data.OrgId);
                if (request.Data.IsShow!=null)
                {
                    q = q.Where(m => m.ISSHOW == request.Data.IsShow);
                }
                q = q.OrderBy(m => m.ID);
                return q;
            });
            return response;
        }
        public BaseResponse<IList<LTC_MakerItem>> QueryMakerItemList(BaseRequest<MakerItemFilter> request)
        {
            var response = base.Query<LTC_MAKERITEM, LTC_MakerItem>(request, (q) =>
            {
                q = q.Where(m => m.QUESTIONID == request.Data.QuestionId);
                return q;
            });
            foreach (var item in response.Data)
            {
                item.MakerItemLimitedValue = base.Query<LTC_MAKERITEMLIMITEDVALUE, LTC_MakerItemLimitedValue>(request, (q) =>
                {
                    q = q.Where(m => m.LIMITEDID == item.LimitedId);
                    return q;
                }).Data;
            }
            return response;
        }
        public BaseResponse<IList<LTC_QuestionResults>> QueryQuestionResultsList(BaseRequest<QuestionResultsFilter> request)
        {
            var response = base.Query<LTC_QUESTIONRESULTS, LTC_QuestionResults>(request, (q) =>
            {
                q = q.Where(m => m.QUESTIONID == request.Data.QuestionId);
                return q;
            });

            return response;
        }
        public BaseResponse<LTC_Question> SaveQuestion(LTC_Question request)
        {
            if (string.IsNullOrEmpty(request.QuestionId.ToString()) || request.QuestionId.Equals(0))
            {
                request.QuestionId = Convert.ToInt32(GenerateCode("", EnumCodeKey.QuestionId));
            }
            return base.Save<LTC_QUESTION, LTC_Question>(request, (q) => q.ID == request.Id);
        }
        public BaseResponse SaveQuestionModleData(int questionId, string orgId, int exportQuestionId)
        {
            var result = new BaseResponse();
            var makerItemList = unitOfWork.GetRepository<LTC_MAKERITEM>().dbSet.Where(m => m.QUESTIONID == questionId).ToList<LTC_MAKERITEM>();
            if (makerItemList.Count > 0)
            {
                foreach (var item in makerItemList)
                {
                    var strSql = string.Format("Delete from LTC_MAKERITEMLIMITEDVALUE where LIMITEDID={0}", item.LIMITEDID);
                    unitOfWork.GetRepository<LTC_MAKERITEMLIMITEDVALUE>().ExecuteSqlCommand(strSql);
                }
            }

            var strSql2 = string.Format("Delete from LTC_MAKERITEM where QUESTIONID={0}", questionId);
            unitOfWork.GetRepository<LTC_MAKERITEM>().ExecuteSqlCommand(strSql2);
            unitOfWork.BeginTransaction();
            try
            {
                var request = new BaseRequest<MakerItemFilter>() { PageSize = 0, Data = new MakerItemFilter { QuestionId = exportQuestionId } };
                var makerItem = base.Query<LTC_MAKERITEM, LTC_MakerItem>(request, (q) =>
                {
                    q = q.Where(m => m.QUESTIONID == request.Data.QuestionId);
                    return q;
                }).Data;
                foreach (var Item in makerItem)
                {
                    var makerItemLimitedValue = base.Query<LTC_MAKERITEMLIMITEDVALUE, LTC_MakerItemLimitedValue>(request, (q) =>
                    {
                        q = q.Where(m => m.LIMITEDID == Item.LimitedId);
                        return q;
                    }).Data;
                    var QueModel = new LTC_MAKERITEM()
                    {
                        QUESTIONID = questionId,
                        LIMITEDID = Convert.ToInt32(GenerateCode("", EnumCodeKey.LimitedId)),
                        CATEGORY = Item.Category,
                        DATATYPE = Item.DataType,
                        MAKENAME = Item.MakeName,
                        SHOWNUMBER = Item.ShowNumber,
                        ISSHOW = Item.IsShow,
                    };
                    unitOfWork.GetRepository<LTC_MAKERITEM>().Insert(QueModel);

                    foreach (var item in makerItemLimitedValue)
                    {
                        var ansModel = new LTC_MAKERITEMLIMITEDVALUE()
                        {
                            LIMITEDID = QueModel.LIMITEDID ?? 0,
                            ISDEFAULT = item.IsDefault,
                            LIMITEDVALUE = item.LimitedValue,
                            LIMITEDVALUENAME = item.LimitedValueName,
                            SHOWNUMBER = item.ShowNumber,
                        };
                        unitOfWork.GetRepository<LTC_MAKERITEMLIMITEDVALUE>().Insert(ansModel);
                    }
                }
                unitOfWork.Commit();
            }
            catch (Exception ex)
            {

            }
            return result;
        }
        public BaseResponse SaveResultModleData(int questionId, string orgId, int exportQuestionId)
        {
            var result = new BaseResponse();
            var strSql3 = string.Format("Delete from LTC_QUESTIONRESULTS where QUESTIONID={0}", questionId);
            unitOfWork.GetRepository<LTC_QUESTIONRESULTS>().ExecuteSqlCommand(strSql3);
            unitOfWork.BeginTransaction();
            try
            {
                var request = new BaseRequest<QuestionResultsFilter>() { PageSize = 0, Data = new QuestionResultsFilter { QuestionId = exportQuestionId } };
                var questionResults = base.Query<LTC_QUESTIONRESULTS, LTC_QuestionResults>(request, (q) =>
                {
                    q = q.Where(m => m.QUESTIONID == request.Data.QuestionId);
                    return q;
                }).Data;
                foreach (var Item in questionResults)
                {
                    var resModel = new LTC_QUESTIONRESULTS()
                    {
                       LOWBOUND=Item.LowBound,
                       UPBOUND=Item.UpBound,
                       RESULTNAME=Item.ResultName,
                       QUESTIONID=questionId
                    };
                    unitOfWork.GetRepository<LTC_QUESTIONRESULTS>().Insert(resModel);

                }
                unitOfWork.Commit();
            }
            catch (Exception ex)
            {

            }
            return result;
        }
        public BaseResponse<LTC_MakerItem> SaveMakerItem(LTC_MakerItem request)
        {
            request.LimitedId = Convert.ToInt32(GenerateCode("", EnumCodeKey.LimitedId));
            return base.Save<LTC_MAKERITEM, LTC_MakerItem>(request, (q) => q.MAKERID == request.MakerId);
        }
        public BaseResponse<LTC_MakerItemLimitedValue> SaveAnswer(LTC_MakerItemLimitedValue request)
        {
            return base.Save<LTC_MAKERITEMLIMITEDVALUE, LTC_MakerItemLimitedValue>(request, (q) => q.LIMITEDVALUEID == request.LimitedValueId);
        }
        public BaseResponse<LTC_QuestionResults> SaveQuestionResults(LTC_QuestionResults request)
        {
            return base.Save<LTC_QUESTIONRESULTS, LTC_QuestionResults>(request, (q) => q.RESULTID == request.ResultId);
        }
        public BaseResponse<List<EvalTempSetModel>> SaveEvalTemplateSet(string orgId, List<EvalTempSetModel> request)
        {
            BaseResponse<List<EvalTempSetModel>> response = new BaseResponse<List<EvalTempSetModel>>();
            var reportRepository = unitOfWork.GetRepository<LTC_QUESTION>();
            var q = from r in reportRepository.dbSet
                    where r.ORGID == orgId
                    select r;

            var oldData = q.ToList();

            Mapper.CreateMap<LTC_Question, LTC_QUESTION>();
            request.ForEach(rs =>
            {
                rs.Items.ForEach(m =>
                {
                    var findItem = oldData.Find(it => it.QUESTIONID == m.QuestionId);
                    if (findItem != null)
                    {
                        if (findItem.ISSHOW != m.Status)
                        {
                            findItem.ISSHOW = m.Status;
                            findItem.ORGID = orgId;
                            reportRepository.Update(findItem);
                        }
                    }
                    else
                    {
                        var model = Mapper.Map<LTC_QUESTION>(m);
                        model.ORGID = orgId;
                        reportRepository.Insert(model);
                    }
                });
            });
            unitOfWork.Save();
            response.Data = request;
            return response;
        }
        public BaseResponse DeleteQuestion(int Id)
        {
            var result = new BaseResponse();
            try
            {
                var question = unitOfWork.GetRepository<LTC_QUESTION>().dbSet.Where(m => m.ID == Id).FirstOrDefault<LTC_QUESTION>();
                if (question != null)
                {
                    var makerItemList = unitOfWork.GetRepository<LTC_MAKERITEM>().dbSet.Where(m => m.QUESTIONID == question.QUESTIONID).ToList<LTC_MAKERITEM>();
                    if (makerItemList.Count > 0)
                    {
                        //批量删除评估问题答案
                        foreach (var item in makerItemList)
                        {
                            var strSql = string.Format("Delete from LTC_MAKERITEMLIMITEDVALUE where LIMITEDID={0}", item.LIMITEDID);
                            unitOfWork.GetRepository<LTC_MAKERITEMLIMITEDVALUE>().ExecuteSqlCommand(strSql);
                        }
                    }
                    //删除评估问题
                    var strSql2 = string.Format("Delete from LTC_MAKERITEM where QUESTIONID={0}", question.QUESTIONID);
                    unitOfWork.GetRepository<LTC_MAKERITEM>().ExecuteSqlCommand(strSql2);

                    //删除评估结果
                    var strSql3 = string.Format("Delete from LTC_QUESTIONRESULTS where QUESTIONID={0}", question.QUESTIONID);
                    unitOfWork.GetRepository<LTC_QUESTIONRESULTS>().ExecuteSqlCommand(strSql3);

                    //删除模板以及使用该模板的评估量表
                    var strSql4 = string.Format("Delete from LTC_QUESTION where QUESTIONID={0}", question.QUESTIONID);
                    unitOfWork.GetRepository<LTC_QUESTION>().ExecuteSqlCommand(strSql4);
                }
                //result = base.Delete<LTC_QUESTION>(Id);
                result.ResultCode = 1;
            }
            catch (Exception ex)
            {
                result.ResultCode = -1;
                throw (ex);
            }
            return result;
        }
        public BaseResponse DeleteMakerItem(int MakerId)
        {
            var result = new BaseResponse();
            try
            {
                var makerItem = unitOfWork.GetRepository<LTC_MAKERITEM>().dbSet.Where(m => m.MAKERID == MakerId).FirstOrDefault<LTC_MAKERITEM>();
                if (makerItem != null)
                {
                    var strSql = string.Format("Delete from LTC_MAKERITEMLIMITEDVALUE where LIMITEDID={0}", makerItem.LIMITEDID);
                    unitOfWork.GetRepository<LTC_MAKERITEMLIMITEDVALUE>().ExecuteSqlCommand(strSql);
                }
                result = base.Delete<LTC_MAKERITEM>(MakerId);
                result.ResultCode = 1;
            }
            catch (Exception ex)
            {
                result.ResultCode = -1;
                throw (ex);
            }
            return result;

        }
        public BaseResponse DeleteAnswer(int LimitedValueId)
        {
            return base.Delete<LTC_MAKERITEMLIMITEDVALUE>(LimitedValueId);
        }
        public BaseResponse DeleteQuestionResults(int ResultId)
        {
            return base.Delete<LTC_QUESTIONRESULTS>(ResultId);
        }
        #endregion

        #region 院内公告
        public BaseResponse<IList<Notice>> QueryNotices(BaseRequest<NoticeFilter> request)
        {
            var response = Query<LTC_NOTIFICATION, Notice>(request, (q) =>
            {
                if (!string.IsNullOrEmpty(request.Data.OrgId))
                {
                    q = q.Where(m => m.ORGID == request.Data.OrgId);
                }
                if (request.Data.SDate.HasValue && request.Data.EDate.HasValue)
                {
                    var endDate = request.Data.EDate.Value.AddDays(1);
                    q = q.Where(m => m.CREATEDATE >= request.Data.SDate && m.CREATEDATE < endDate);
                }
                else if (request.Data.SDate.HasValue)
                {
                    q = q.Where(m => m.CREATEDATE >= request.Data.SDate);
                }
                else if (request.Data.EDate.HasValue)
                {
                    var endDate = request.Data.EDate.Value.AddDays(1);
                    q = q.Where(m => m.CREATEDATE < endDate);
                }
                q = q.OrderByDescending(m => m.CREATEDATE);
                return q;
            });
            return response;
        }

        public BaseResponse<Notice> GetNotice(int id)
        {
            return base.Get<LTC_NOTIFICATION, Notice>((q) => q.ID == id);
        }

        public BaseResponse<Notice> SaveNotice(Notice request)
        {
            return base.Save<LTC_NOTIFICATION, Notice>(request, (q) => q.ID == request.Id);
        }

        public BaseResponse DeleteNotice(int id)
        {
            return Delete<LTC_NOTIFICATION>(id);
        }
        #endregion
        /// <summary>
        /// 在院人数男女比例
        /// </summary>
        /// <returns></returns>
        public object GetPersonNum()
        {

            var q = from item in
                        (from r in unitOfWork.GetRepository<LTC_IPDREG>().dbSet
                         where r.IPDFLAG == "I" && r.ORGID == SecurityHelper.CurrentPrincipal.OrgId
                         join f in unitOfWork.GetRepository<LTC_REGFILE>().dbSet on r.REGNO equals f.REGNO
                         select new {
                             Sex = f.SEX.Replace("F", "女").Replace("M", "男")
                         })
                    group item by item.Sex into g
                    select new
                    {
                        label = g.Key,
                        data = g.Count()
                    };
            return q.ToList();
        }
        /// <summary>
        /// 护理险分布
        /// </summary>
        /// <returns></returns>
        public object GetNursingRisk()
        {

            var q = from item in ((from c in unitOfWork.GetRepository<LTC_IPDREG>().dbSet where c.SERVICETYPE=="013" select new { Type="公费"}).Concat
                ((from e in unitOfWork.GetRepository<LTC_IPDREG>().dbSet where e.SERVICETYPE != "013" select new { Type = "自费" })))
                         group item by item.Type into g
                    select new
                    {
                        label = g.Key,
                        data = g.Count()
                    };
            return q.ToList();
        }
        /// <summary>
        /// 费用统计
        /// </summary>
        /// <returns></returns>
        public object GetCostDtl()
        {

            var q = from c in unitOfWork.GetRepository<LTC_COSTDTL>().dbSet
                    where c.OCCURTIME != null && c.OCCURTIME.Value.Year == DateTime.Now.Year
                    select c;
            var data = q.ToList();
           var m=  data.GroupBy(item => item.OCCURTIME.Value.Month).Select(item => new
            {
                month = item.Key,
                cost=item.Sum(s=>s.TOTALPRICE)
            }).ToList();
           return m;
        }
        /// <summary>
        /// 会员年龄分布
        /// </summary>
        /// <returns></returns>
        public object GetAgeDistribution()
        {

            var q = from r in unitOfWork.GetRepository<LTC_REGFILE>().dbSet
                    where r.BRITHDATE != null && (DateTime.Now.Year - r.BRITHDATE.Value.Year) >= 51 && (DateTime.Now.Year - r.BRITHDATE.Value.Year) <= 200
                    group r by r.BRITHDATE into g
                    select new {
                        Age = g.Key,
                        Distribution=g.Count()
                    };
            var data = q.ToList();

            int[] Start = { 51, 61, 71, 81, 91, 100 };
            int[] End = { 60, 70, 80, 90, 100, 200 };
            List<ArrayList> arrlist = new List<ArrayList>();
            for (int i = 0; i < 5; i++)
            {
                ArrayList arr = new ArrayList();
                arr.Add(Start[i] + "-" + End[i]);
                arr.Add(data.Where(w => Util.GetAgeByDate(w.Age) >= Start[i] && Util.GetAgeByDate(w.Age)<End[i]).Sum(s=>s.Distribution));
                arrlist.Add(arr);
            }
            return arrlist;
        }
    }
}