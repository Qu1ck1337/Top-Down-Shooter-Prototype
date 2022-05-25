using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyComponent : UnitComponent
{
    [Space, SerializeField]
    private Enums.EnemyType EnemyType = Enums.EnemyType.Weakling;

    [Space, SerializeField, Range(0f, 100f)]
    private float _playerIdentificationRadius;
    [SerializeField]
    private float _timeForPlayerPursuitInIdentificationRadius = 2f;

    public float GetPlayerIdentificationRadius => _playerIdentificationRadius;

    [SerializeField, Range(0f, 100f)]
    private float _radiusOfEnemyView;

    [Space, SerializeField]
    private float _delayTimeAfterFire;

    [SerializeField, Space]
    private List<Vector3> _patrollingPoints = new List<Vector3>();
    private int _currentPartollingPointIndex;
    [SerializeField]
    private float _stayOnRadiusPatrollingPoint;
    [SerializeField]
    private float _delayStayingOnPoint;
    [SerializeField]
    private float _delayStayingOnPointOffset;

    [Space, SerializeField]
    private float _pursuitSpeed;

    [Space, SerializeField]
    private float _distanceToHandAttack = 0.2f;

    private Transform _target;
    private NavMeshAgent _agent;
    private ProjectilePool _projectilePool;
    private bool _inCooldown;
    private bool _isTargetInIdentificationRadius;

    [Space, SerializeField]
    private bool _showGizmos;

    public Enums.EnemyStateType StateType { get; private set; } = Enums.EnemyStateType.Idle;

    private void Start()
    {
        _handTrigger = GetComponentInChildren<SphereCollider>();

        if (EnemyType != Enums.EnemyType.Fat && _weapon != null)
        {
            _weapon = Instantiate(_weapon, transform);
            _weapon.transform.position = _weaponSpawn + transform.localPosition;
            _weapon.Owner = this;
        }

        _agent = GetComponent<NavMeshAgent>();
        _target = FindObjectOfType<PlayerComponent>().gameObject.transform;
        _projectilePool = FindObjectOfType<ProjectilePool>();
        if (_patrollingPoints.Count > 0)
        {
            _patrollingPoints.Add(transform.position);
            StateType = Enums.EnemyStateType.Patrolling;
        }
        else
        {
            _patrollingPoints.Add(transform.position);
        }
    }

    private void Update()
    {
        if (_target == null) return;
        if (_inCooldown) return;
        TargetDetection();
        if (!_isMoving) return;
        UpdateStatus();
    }

    private void FixedUpdate()
    {
        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        var velocity = _agent.velocity.normalized;

        if (velocity.x < 0.5f && velocity.x > -0.5f && velocity.z < 0.5f && velocity.z > -0.5f) _animator.SetBool("IsMoving", false);
        else
        {
            _animator.SetBool("IsMoving", true);
            _animator.SetFloat("HorizontalMoving", velocity.x);
            _animator.SetFloat("VerticalMoving", velocity.z);
        }
    }

    private bool _isMovingOnPatrolling = true;
    private bool _isMoving = true;
    private void UpdateStatus()
    {
        switch (StateType)
        {
            case Enums.EnemyStateType.Idle:
                _agent.destination = transform.position;
                break;
            case Enums.EnemyStateType.Patrolling:
                if (!_isMovingOnPatrolling) return;
                _agent.speed = _movementSpeed;
                var ind = _currentPartollingPointIndex % _patrollingPoints.Count;
                _agent.destination = _patrollingPoints[ind];
                if (Vector3.Distance(new Vector3(_patrollingPoints[ind].x, transform.position.y, _patrollingPoints[ind].z), transform.position) < _stayOnRadiusPatrollingPoint)
                {
                    _isMovingOnPatrolling = false;
                    StartCoroutine(ChangePatrollingPoint());
                }
                break;
            case Enums.EnemyStateType.Pursuit:
                _agent.destination = _target.position;
                _agent.speed = _pursuitSpeed;
                FireLogic();
                break;
        }
    }

    private void FireLogic()
    {
        RaycastHit hit;
        Physics.Raycast(transform.position, transform.forward, out hit);
        if (_weapon != null && Vector3.Distance(transform.position, _target.position) < _weapon.GetRadiusToFire() && hit.collider != null && hit.collider.GetComponent<PlayerComponent>() != null)
        {
            _weapon.checkAndFire();
            _isMoving = false;
            StartCoroutine(StopAfterFire());

            if (_weapon.CurrentAllAmmo <= 0 && _weapon.CurrentAmmoInStore <= 0)
                DropWeapon();
        }
        else if (!_inAnimation && Vector3.Distance(transform.position, _target.transform.position) <= _distanceToHandAttack && hit.collider != null && hit.collider.GetComponent<PlayerComponent>() != null)
        {
            _animator.SetTrigger("HandAttack");
            _inAnimation = true;
        }
    }

    private IEnumerator StopAfterFire()
    {
        _agent.destination = transform.position;
        yield return new WaitForSeconds(_delayTimeAfterFire);
        //if (!(_weapon.CurrentAllAmmo <= 0 && _weapon.CurrentAmmoInStore <= 0))
        _isMoving = true;
    }

    private void TargetDetection()
    {
        if (Vector3.Distance(transform.position, _target.position) < _playerIdentificationRadius)
        {
            if (!_isTargetInIdentificationRadius && StateType != Enums.EnemyStateType.Pursuit)
            {
                _isTargetInIdentificationRadius = true;
                StartCoroutine(WaitForTargetStayingInIdentificationRadius());
            }
        }
        else if (Vector3.Distance(transform.position, _target.position) < _radiusOfEnemyView && IsAnyBulletsAround())
        {
            StateType = Enums.EnemyStateType.Pursuit;
        }
        else if (Vector3.Distance(transform.position, _target.position) >= _radiusOfEnemyView)
        {
            StateType = Enums.EnemyStateType.Patrolling;
        }
        else
        {
            _isTargetInIdentificationRadius = false;
        }
    }

    private IEnumerator WaitForTargetStayingInIdentificationRadius()
    {
        yield return new WaitForSeconds(_timeForPlayerPursuitInIdentificationRadius);
        if (Vector3.Distance(transform.position, _target.position) < _playerIdentificationRadius)
            StateType = Enums.EnemyStateType.Pursuit;
    }

    private IEnumerator ChangePatrollingPoint() 
    {
        yield return new WaitForSeconds(Mathf.Clamp(_delayStayingOnPoint + UnityEngine.Random.Range(-_delayStayingOnPointOffset, _delayStayingOnPointOffset), 0f, float.MaxValue));
        _currentPartollingPointIndex += 1;
        _isMovingOnPatrolling = true;
    }

    private bool IsAnyBulletsAround()
    {
        var projectile = _projectilePool.GetNearestProjectileInEnemyRadius(this);
        if (projectile != null && projectile.Owner is PlayerComponent)
        {
            return true;
        }
        return false;
    }

    public void SetEnemyCooldown(float seconds)
    {
        StartCoroutine(SetCooldown(seconds));
    }

    private IEnumerator SetCooldown(float seconds)
    {
        _inCooldown = true;
        yield return new WaitForSeconds(seconds);
        _inCooldown = false;
    }

    private void OnDrawGizmosSelected()
    {
        foreach (var point in _patrollingPoints)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(point, 1f);
        }

        if (!_showGizmos) return;

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(transform.position, _playerIdentificationRadius);

        Gizmos.color = Color.gray;
        Gizmos.DrawSphere(transform.position, _radiusOfEnemyView);

        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(_weaponSpawn + transform.position, 0.1f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (EnemyType == Enums.EnemyType.Fat || _weapon != null) return;
        var weaponComponent = collision.gameObject.GetComponent<WeaponComponent>();
        if (weaponComponent != null && weaponComponent.Owner == null)
        {
            _weapon = weaponComponent;
            _weapon.Owner = this;
            _weapon.WeaponRigidBody.isKinematic = true;
            _weapon.transform.parent = transform;
            _weapon.transform.localPosition = _weaponSpawn;
            _weapon.transform.rotation = transform.rotation;
        }
    }
}
